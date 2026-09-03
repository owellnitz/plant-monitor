//! Streams a firmware image from the backend into the spare app slot.
//!
//! The download is never buffered whole — the image is ~440 KB and the device
//! has ~100 KB of heap — so bytes go to the sink as they arrive. Nothing is
//! activated here: the caller only swaps slots once the image has been fully
//! written and verified, so a failed update costs one wake cycle and leaves
//! the running firmware untouched.

use embedded_io::{Read, ReadReady};
use sha2::{Digest, Sha256};

/// Read size per socket call. The sink re-blocks for flash alignment, so this
/// only trades syscalls against stack.
const CHUNK: usize = 512;

#[derive(Debug)]
pub enum Error {
    Io,
    /// The connection ended before `size` bytes arrived.
    Truncated,
    /// More bytes were already buffered than the image is meant to contain.
    TooLarge,
    /// The download stalled past the deadline.
    Timeout,
    /// The sink refused a write (on the device: a failed flash write).
    Sink,
    /// The image hashed to something other than what the backend advertised.
    HashMismatch,
    /// The OTA slot bookkeeping in flash could not be read or updated.
    Slot,
}

/// What the backend said the image should be, from `GET /api/firmware/latest`.
pub struct Expected<'a> {
    pub size: u32,
    /// Lowercase hex sha256, as served by `/api/firmware/latest`.
    pub sha256: &'a str,
}

/// Where a downloaded image is written. Implemented over the spare flash slot
/// on the device and over a plain buffer in tests; chunk sizes are whatever
/// the socket produced, so an implementation that needs alignment re-blocks
/// internally.
pub trait ImageSink {
    type Error;

    fn write(&mut self, chunk: &[u8]) -> Result<(), Self::Error>;
}

/// Streams exactly `expected.size` bytes into `sink`.
///
/// `prefix` is the body bytes [`crate::http::get`] already read while parsing
/// headers; the rest is pulled from the socket. Polls `read_ready` against a
/// deadline for the same reason the HTTP and MQTT clients do: a blocking read
/// on a stalled connection would hold an unattended device awake until the
/// watchdog bites.
pub fn download<S: Read + ReadReady, K: ImageSink>(
    socket: &mut S,
    prefix: &[u8],
    expected: &Expected,
    sink: &mut K,
    now_ms: impl Fn() -> u64,
    timeout_ms: u64,
) -> Result<(), Error> {
    let size = expected.size as usize;
    if prefix.len() > size {
        return Err(Error::TooLarge);
    }

    let mut hasher = Sha256::new();
    let mut written = 0usize;
    if !prefix.is_empty() {
        sink.write(prefix).map_err(|_| Error::Sink)?;
        hasher.update(prefix);
        written = prefix.len();
    }

    let deadline = now_ms() + timeout_ms;
    let mut buf = [0u8; CHUNK];

    while written < size {
        // Never read past the image: the server may keep the connection open,
        // and an over-read would block waiting for bytes that never come.
        let want = (size - written).min(CHUNK);

        match socket.read_ready() {
            Ok(true) => {
                let n = socket.read(&mut buf[..want]).map_err(|_| Error::Io)?;
                // HTTP/1.0 signals the end by closing, so a close short of the
                // advertised length means the image is incomplete.
                if n == 0 {
                    return Err(Error::Truncated);
                }
                sink.write(&buf[..n]).map_err(|_| Error::Sink)?;
                hasher.update(&buf[..n]);
                written += n;
            }
            Ok(false) if now_ms() >= deadline => return Err(Error::Timeout),
            Ok(false) => {}
            Err(_) => return Err(Error::Io),
        }
    }

    // The image is in the spare slot but nothing points at it yet, so a
    // mismatch here just means this cycle wrote junk that the next download
    // overwrites — the running firmware is untouched either way.
    if hex_eq(&hasher.finalize(), expected.sha256) {
        Ok(())
    } else {
        Err(Error::HashMismatch)
    }
}

/// Flash sector size. Writes are re-blocked to this because esp-storage does a
/// read-modify-erase-write of the whole sector per call: forwarding 512-byte
/// socket chunks straight through would erase every sector eight times over,
/// costing eight times the wear and most of a minute for a 441 KB image.
pub const SECTOR: usize = 4096;

/// Somewhere whole sectors can be written at a byte offset — the spare app
/// slot on the device, a plain buffer in tests.
pub trait BlockWriter {
    fn write_at(&mut self, offset: u32, bytes: &[u8]) -> Result<(), Error>;
}

/// Collects the socket's arbitrary chunks into whole sectors before writing.
///
/// The caller owns the buffer: 4 KB is more than this device wants to put on
/// the stack.
pub struct SectorSink<'a, W: BlockWriter> {
    writer: W,
    buf: &'a mut [u8; SECTOR],
    filled: usize,
    offset: u32,
}

impl<'a, W: BlockWriter> SectorSink<'a, W> {
    pub fn new(writer: W, buf: &'a mut [u8; SECTOR]) -> Self {
        SectorSink {
            writer,
            buf,
            filled: 0,
            offset: 0,
        }
    }

    /// Writes the trailing partial sector. An image is almost never a whole
    /// number of sectors, so skipping this would truncate it.
    pub fn finish(&mut self) -> Result<(), Error> {
        self.flush()
    }

    fn flush(&mut self) -> Result<(), Error> {
        if self.filled == 0 {
            return Ok(());
        }
        self.writer
            .write_at(self.offset, &self.buf[..self.filled])?;
        self.offset += self.filled as u32;
        self.filled = 0;
        Ok(())
    }
}

impl<W: BlockWriter> ImageSink for SectorSink<'_, W> {
    type Error = Error;

    fn write(&mut self, mut chunk: &[u8]) -> Result<(), Error> {
        while !chunk.is_empty() {
            let take = chunk.len().min(SECTOR - self.filled);
            self.buf[self.filled..self.filled + take].copy_from_slice(&chunk[..take]);
            self.filled += take;
            chunk = &chunk[take..];

            if self.filled == SECTOR {
                self.flush()?;
            }
        }
        Ok(())
    }
}

/// The spare app slot as a [`BlockWriter`].
#[cfg(feature = "net")]
pub struct FlashSlot<'a, F: embedded_storage::Storage>(
    pub esp_bootloader_esp_idf::partitions::FlashRegion<'a, F>,
);

#[cfg(feature = "net")]
impl<F: embedded_storage::Storage> BlockWriter for FlashSlot<'_, F> {
    fn write_at(&mut self, offset: u32, bytes: &[u8]) -> Result<(), Error> {
        embedded_storage::Storage::write(&mut self.0, offset, bytes).map_err(|_| Error::Sink)
    }
}

/// Points the bootloader at the slot just written, for the next boot.
///
/// Call this only after [`download`] returned `Ok` — until it does, the spare
/// slot holds an unverified image and the device must keep booting the one it
/// is running. The new slot is marked `New` rather than `Valid` so the
/// bootloader watches its first boot: if that boot never confirms the image,
/// it rolls back instead of leaving the device stranded on firmware that
/// cannot come up.
#[cfg(feature = "net")]
pub fn activate<F: embedded_storage::Storage>(
    updater: &mut esp_bootloader_esp_idf::ota_updater::OtaUpdater<'_, F>,
) -> Result<(), Error> {
    updater.activate_next_partition().map_err(|_| Error::Slot)?;
    updater
        .set_current_ota_state(esp_bootloader_esp_idf::ota::OtaImageState::New)
        .map_err(|_| Error::Slot)
}

/// Compares a digest against its lowercase-hex spelling without allocating.
fn hex_eq(digest: &[u8], hex: &str) -> bool {
    const HEX: &[u8; 16] = b"0123456789abcdef";

    // A trailing odd byte means the value was not full hex pairs at all.
    let (pairs, rest) = hex.as_bytes().as_chunks::<2>();
    rest.is_empty()
        && pairs.len() == digest.len()
        && digest.iter().zip(pairs).all(|(b, h)| {
            h[0].to_ascii_lowercase() == HEX[usize::from(b >> 4)]
                && h[1].to_ascii_lowercase() == HEX[usize::from(b & 0x0f)]
        })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Socket that serves canned bytes, optionally a few at a time, and can
    /// either go quiet or signal end-of-stream once drained.
    struct MockSocket {
        to_read: Vec<u8>,
        read_pos: usize,
        chunk: usize,
        closes: bool,
    }

    impl MockSocket {
        fn new(to_read: &[u8]) -> Self {
            MockSocket {
                to_read: to_read.to_vec(),
                read_pos: 0,
                chunk: usize::MAX,
                closes: false,
            }
        }

        fn in_chunks(to_read: &[u8], chunk: usize) -> Self {
            MockSocket {
                chunk,
                ..MockSocket::new(to_read)
            }
        }

        /// Reports readable and reads 0 bytes once drained — a closed peer.
        fn then_closes(to_read: &[u8]) -> Self {
            MockSocket {
                closes: true,
                ..MockSocket::new(to_read)
            }
        }
    }

    impl embedded_io::ErrorType for MockSocket {
        type Error = embedded_io::ErrorKind;
    }

    impl Read for MockSocket {
        fn read(&mut self, buf: &mut [u8]) -> Result<usize, Self::Error> {
            let remaining = &self.to_read[self.read_pos..];
            let n = remaining.len().min(buf.len()).min(self.chunk);
            buf[..n].copy_from_slice(&remaining[..n]);
            self.read_pos += n;
            Ok(n)
        }
    }

    impl ReadReady for MockSocket {
        fn read_ready(&mut self) -> Result<bool, Self::Error> {
            Ok(self.closes || self.read_pos < self.to_read.len())
        }
    }

    /// Collects everything written, so a test can compare against the image.
    #[derive(Default)]
    struct VecSink {
        written: Vec<u8>,
        fail_after: Option<usize>,
    }

    impl ImageSink for VecSink {
        type Error = ();

        fn write(&mut self, chunk: &[u8]) -> Result<(), Self::Error> {
            if self.fail_after.is_some_and(|at| self.written.len() >= at) {
                return Err(());
            }
            self.written.extend_from_slice(chunk);
            Ok(())
        }
    }

    fn frozen_clock() -> impl Fn() -> u64 {
        || 0
    }

    fn ticking_clock() -> impl Fn() -> u64 {
        let t = core::cell::Cell::new(0u64);
        move || {
            let v = t.get();
            t.set(v + 1000);
            v
        }
    }

    fn image(len: usize) -> Vec<u8> {
        (0..len).map(|i| (i % 251) as u8).collect()
    }

    /// The hash the backend would have advertised for this image.
    fn sha_of(bytes: &[u8]) -> String {
        Sha256::digest(bytes)
            .iter()
            .map(|b| format!("{b:02x}"))
            .collect()
    }

    /// For the paths that fail before the digest is ever compared.
    const UNUSED_SHA: &str = "00000000000000000000000000000000000000000000000000000000000000ff";

    #[test]
    fn writes_the_whole_image_from_the_socket() {
        let img = image(2000);
        let mut socket = MockSocket::new(&img);
        let mut sink = VecSink::default();

        download(
            &mut socket,
            &[],
            &Expected {
                size: 2000,
                sha256: &sha_of(&img),
            },
            &mut sink,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(sink.written, img);
    }

    // http::get hands back the body bytes that arrived with the headers; they
    // are part of the image and must land in the sink first.
    #[test]
    fn writes_the_header_prefix_before_the_socket_bytes() {
        let img = image(1000);
        let (prefix, rest) = img.split_at(37);
        let mut socket = MockSocket::new(rest);
        let mut sink = VecSink::default();

        download(
            &mut socket,
            prefix,
            &Expected {
                size: 1000,
                sha256: &sha_of(&img),
            },
            &mut sink,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(sink.written, img);
    }

    // The image is many times the read buffer, so the loop has to reassemble
    // it across reads without dropping or duplicating a chunk.
    #[test]
    fn reassembles_an_image_delivered_in_small_pieces() {
        let img = image(3000);
        let mut socket = MockSocket::in_chunks(&img, 7);
        let mut sink = VecSink::default();

        download(
            &mut socket,
            &[],
            &Expected {
                size: 3000,
                sha256: &sha_of(&img),
            },
            &mut sink,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(sink.written, img);
    }

    // The server may hold the connection open after the body; reading past the
    // advertised length would block for bytes that never come.
    #[test]
    fn stops_at_the_advertised_size() {
        let mut socket = MockSocket::new(&image(5000));
        let mut sink = VecSink::default();

        download(
            &mut socket,
            &[],
            &Expected {
                size: 1500,
                sha256: &sha_of(&image(1500)),
            },
            &mut sink,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(sink.written.len(), 1500);
    }

    #[test]
    fn rejects_a_connection_closed_before_the_end() {
        let mut socket = MockSocket::then_closes(&image(400));
        let mut sink = VecSink::default();

        assert!(matches!(
            download(
                &mut socket,
                &[],
                &Expected {
                    size: 1000,
                    sha256: UNUSED_SHA
                },
                &mut sink,
                frozen_clock(),
                1000
            ),
            Err(Error::Truncated)
        ));
    }

    #[test]
    fn times_out_on_a_stalled_download() {
        let mut socket = MockSocket::new(&image(400));
        let mut sink = VecSink::default();

        assert!(matches!(
            download(
                &mut socket,
                &[],
                &Expected {
                    size: 1000,
                    sha256: UNUSED_SHA
                },
                &mut sink,
                ticking_clock(),
                1000
            ),
            Err(Error::Timeout)
        ));
    }

    #[test]
    fn rejects_a_prefix_longer_than_the_image() {
        let mut socket = MockSocket::new(&[]);
        let mut sink = VecSink::default();

        assert!(matches!(
            download(
                &mut socket,
                &image(200),
                &Expected {
                    size: 100,
                    sha256: UNUSED_SHA
                },
                &mut sink,
                frozen_clock(),
                1000
            ),
            Err(Error::TooLarge)
        ));
    }

    // A failing flash write must abort the update, not be written off.
    #[test]
    fn reports_a_sink_failure() {
        let mut socket = MockSocket::new(&image(2000));
        let mut sink = VecSink {
            fail_after: Some(600),
            ..VecSink::default()
        };

        assert!(matches!(
            download(
                &mut socket,
                &[],
                &Expected {
                    size: 2000,
                    sha256: UNUSED_SHA
                },
                &mut sink,
                frozen_clock(),
                1000
            ),
            Err(Error::Sink)
        ));
    }

    // The whole point of the hash: a complete download of the wrong bytes is
    // refused, so a corrupted image never gets activated.
    #[test]
    fn rejects_an_image_that_hashes_differently() {
        let mut corrupted = image(2000);
        corrupted[1234] ^= 0xff;
        let mut socket = MockSocket::new(&corrupted);
        let mut sink = VecSink::default();

        assert!(matches!(
            download(
                &mut socket,
                &[],
                &Expected {
                    size: 2000,
                    sha256: &sha_of(&image(2000))
                },
                &mut sink,
                frozen_clock(),
                1000
            ),
            Err(Error::HashMismatch)
        ));
    }

    #[test]
    fn rejects_a_hash_of_the_wrong_length() {
        let img = image(500);
        let mut socket = MockSocket::new(&img);
        let mut sink = VecSink::default();
        let truncated = &sha_of(&img)[..63];

        assert!(matches!(
            download(
                &mut socket,
                &[],
                &Expected {
                    size: 500,
                    sha256: truncated
                },
                &mut sink,
                frozen_clock(),
                1000
            ),
            Err(Error::HashMismatch)
        ));
    }

    // The backend serves lowercase, but nothing in the contract forbids a
    // server from spelling it the other way.
    #[test]
    fn accepts_an_uppercase_hash() {
        let img = image(500);
        let mut socket = MockSocket::new(&img);
        let mut sink = VecSink::default();

        download(
            &mut socket,
            &[],
            &Expected {
                size: 500,
                sha256: &sha_of(&img).to_uppercase(),
            },
            &mut sink,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(sink.written, img);
    }

    /// Records each write as (offset, bytes), so a test can check both the
    /// reassembled image and how it was blocked up.
    #[derive(Default)]
    struct RecordingWriter {
        writes: Vec<(u32, Vec<u8>)>,
    }

    impl BlockWriter for RecordingWriter {
        fn write_at(&mut self, offset: u32, bytes: &[u8]) -> Result<(), Error> {
            self.writes.push((offset, bytes.to_vec()));
            Ok(())
        }
    }

    /// Drives a SectorSink with the given chunk sizes and returns the writes.
    fn sector_writes(total: usize, chunk: usize) -> Vec<(u32, Vec<u8>)> {
        let img = image(total);
        let mut buf = [0u8; SECTOR];
        let mut sink = SectorSink::new(RecordingWriter::default(), &mut buf);
        for piece in img.chunks(chunk) {
            sink.write(piece).unwrap();
        }
        sink.finish().unwrap();
        sink.writer.writes
    }

    // esp-storage erases a whole sector per write, so anything short of full
    // sectors multiplies flash wear and download time.
    #[test]
    fn sectors_are_written_whole_and_in_order() {
        let writes = sector_writes(SECTOR * 3, 512);

        assert_eq!(writes.len(), 3);
        for (i, (offset, bytes)) in writes.iter().enumerate() {
            assert_eq!(*offset as usize, i * SECTOR);
            assert_eq!(bytes.len(), SECTOR);
        }
    }

    // An image is almost never a whole number of sectors; losing the tail
    // would write a truncated image that then fails its hash.
    #[test]
    fn the_trailing_partial_sector_is_written() {
        let writes = sector_writes(SECTOR * 2 + 100, 512);

        assert_eq!(writes.len(), 3);
        assert_eq!(writes[2].0 as usize, SECTOR * 2);
        assert_eq!(writes[2].1.len(), 100);
    }

    // Whatever the socket hands over — a byte at a time or more than a whole
    // sector at once — the bytes must land in order and unchanged.
    #[test]
    fn reassembles_the_image_whatever_the_chunk_size() {
        let total = SECTOR * 2 + 777;
        let expected = image(total);

        for chunk in [1, 7, 512, SECTOR, SECTOR + 3, total] {
            let rebuilt: Vec<u8> = sector_writes(total, chunk)
                .into_iter()
                .flat_map(|(_, bytes)| bytes)
                .collect();
            assert_eq!(rebuilt, expected, "chunk size {chunk}");
        }
    }

    #[test]
    fn an_empty_image_writes_nothing() {
        assert!(sector_writes(0, 512).is_empty());
    }

    // Guards the hex comparison itself: every nibble position must be checked,
    // not just the bytes that happen to differ in the first place.
    #[test]
    fn hex_comparison_matches_only_the_exact_digest() {
        let digest = Sha256::digest(b"plant-monitor");
        let hex = sha_of(b"plant-monitor");

        assert!(hex_eq(&digest, &hex));
        for at in [0, 1, 31, 63] {
            let mut wrong: Vec<char> = hex.chars().collect();
            wrong[at] = if wrong[at] == '0' { '1' } else { '0' };
            let wrong: String = wrong.into_iter().collect();
            assert!(!hex_eq(&digest, &wrong), "nibble {at} not compared");
        }
    }
}
