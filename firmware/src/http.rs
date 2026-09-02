//! Minimal HTTP/1.0 client for the OTA update check and image download.
//!
//! HTTP/1.0 on purpose: the server closes the connection when the body ends,
//! so there is no chunked encoding or keep-alive bookkeeping to implement.
//! Plain HTTP only — the backend serves these on the local network because the
//! chip has no TLS.

use embedded_io::{Read, ReadReady, Write};

#[derive(Debug)]
pub enum Error {
    Io,
    /// Response did not start with a parsable `HTTP/1.x <status>` line.
    BadResponse,
    /// The headers did not fit in the caller's buffer.
    HeadersTooLarge,
    /// The response headers did not arrive within the timeout.
    Timeout,
}

/// A response with its headers parsed. The body is not buffered: the device
/// streams a ~440 KB image straight to flash, so `body` holds only the bytes
/// that happened to arrive alongside the headers and the caller reads the rest
/// from the socket itself.
pub struct Response<'a> {
    pub status: u16,
    pub body: &'a [u8],
}

/// Sends `GET path` and parses the response headers into `buf`, giving up
/// after `timeout_ms`.
///
/// Like the MQTT client, this polls `read_ready` against a deadline rather
/// than calling `read` directly: the blocking socket spins forever when no
/// bytes are buffered, which would hold the device awake until the watchdog
/// bites.
pub fn get<'a, S: Read + Write + ReadReady>(
    socket: &mut S,
    host: &str,
    path: &str,
    buf: &'a mut [u8],
    now_ms: impl Fn() -> u64,
    timeout_ms: u64,
) -> Result<Response<'a>, Error> {
    for part in [
        b"GET ".as_slice(),
        path.as_bytes(),
        b" HTTP/1.0\r\nHost: ",
        host.as_bytes(),
        b"\r\n\r\n",
    ] {
        socket.write_all(part).map_err(|_| Error::Io)?;
    }
    socket.flush().map_err(|_| Error::Io)?;

    let (header_end, filled) = read_headers(socket, buf, now_ms, timeout_ms)?;
    let status = parse_status(&buf[..header_end])?;

    Ok(Response {
        status,
        body: &buf[header_end..filled],
    })
}

/// Reads until the blank line that ends the headers. Returns where the body
/// starts and how much of `buf` is filled — the surplus is body bytes that
/// arrived in the same packet.
fn read_headers<S: Read + ReadReady>(
    socket: &mut S,
    buf: &mut [u8],
    now_ms: impl Fn() -> u64,
    timeout_ms: u64,
) -> Result<(usize, usize), Error> {
    let deadline = now_ms() + timeout_ms;
    let mut filled = 0;
    // Only the bytes a new read could complete the terminator across need
    // re-scanning, so resume 3 bytes back rather than from the start.
    let mut scanned = 0;

    loop {
        if let Some(at) = find_blank_line(&buf[scanned..filled]) {
            return Ok((scanned + at, filled));
        }
        scanned = filled.saturating_sub(3);

        if filled == buf.len() {
            return Err(Error::HeadersTooLarge);
        }

        match socket.read_ready() {
            Ok(true) => {
                let n = socket.read(&mut buf[filled..]).map_err(|_| Error::Io)?;
                // HTTP/1.0: a close before the blank line means no more is coming.
                if n == 0 {
                    return Err(Error::BadResponse);
                }
                filled += n;
            }
            Ok(false) if now_ms() >= deadline => return Err(Error::Timeout),
            Ok(false) => {}
            Err(_) => return Err(Error::Io),
        }
    }
}

/// Index just past the `\r\n\r\n` that ends the headers.
fn find_blank_line(bytes: &[u8]) -> Option<usize> {
    bytes
        .windows(4)
        .position(|w| w == b"\r\n\r\n")
        .map(|at| at + 4)
}

/// `HTTP/1.1 200 OK` -> 200. The reason phrase is ignored.
fn parse_status(headers: &[u8]) -> Result<u16, Error> {
    let line = headers
        .split(|&b| b == b'\r')
        .next()
        .ok_or(Error::BadResponse)?;
    if !line.starts_with(b"HTTP/1.") {
        return Err(Error::BadResponse);
    }
    let code = line
        .split(|&b| b == b' ')
        .nth(1)
        .ok_or(Error::BadResponse)?;
    if code.len() != 3 {
        return Err(Error::BadResponse);
    }
    code.iter().try_fold(0u16, |acc, &b| {
        b.is_ascii_digit()
            .then(|| acc * 10 + u16::from(b - b'0'))
            .ok_or(Error::BadResponse)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// In-memory socket: records writes, serves canned read bytes. Mirrors the
    /// one in mqtt.rs, with a chunk size so a response can be delivered in
    /// pieces the way a real socket does.
    struct MockSocket {
        written: Vec<u8>,
        to_read: Vec<u8>,
        read_pos: usize,
        chunk: usize,
    }

    impl MockSocket {
        fn with_response(to_read: &[u8]) -> Self {
            MockSocket {
                written: Vec::new(),
                to_read: to_read.to_vec(),
                read_pos: 0,
                chunk: usize::MAX,
            }
        }

        /// Delivers at most `chunk` bytes per read.
        fn in_chunks(to_read: &[u8], chunk: usize) -> Self {
            MockSocket {
                chunk,
                ..MockSocket::with_response(to_read)
            }
        }
    }

    impl embedded_io::ErrorType for MockSocket {
        type Error = embedded_io::ErrorKind;
    }

    impl Write for MockSocket {
        fn write(&mut self, buf: &[u8]) -> Result<usize, Self::Error> {
            self.written.extend_from_slice(buf);
            Ok(buf.len())
        }

        fn flush(&mut self) -> Result<(), Self::Error> {
            Ok(())
        }
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

    impl embedded_io::ReadReady for MockSocket {
        fn read_ready(&mut self) -> Result<bool, Self::Error> {
            Ok(self.read_pos < self.to_read.len())
        }
    }

    /// Clock that never advances — for responses already buffered, where the
    /// deadline is never consulted.
    fn frozen_clock() -> impl Fn() -> u64 {
        || 0
    }

    #[test]
    fn get_sends_a_http_1_0_request() {
        let mut socket = MockSocket::with_response(b"HTTP/1.1 200 OK\r\n\r\n");
        let mut buf = [0u8; 256];
        get(
            &mut socket,
            "192.168.1.10:5001",
            "/api/firmware/latest?current=firmware-v0.3.0",
            &mut buf,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(
            socket.written,
            b"GET /api/firmware/latest?current=firmware-v0.3.0 HTTP/1.0\r\nHost: 192.168.1.10:5001\r\n\r\n"
        );
    }

    #[test]
    fn get_parses_the_status_code() {
        let mut socket = MockSocket::with_response(b"HTTP/1.1 204 No Content\r\n\r\n");
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.status, 204);
        assert!(response.body.is_empty());
    }

    // The terminator can straddle two reads, so the scan must not restart
    // from the current end each time.
    #[test]
    fn get_handles_headers_split_across_reads() {
        let mut socket = MockSocket::in_chunks(b"HTTP/1.1 200 OK\r\nX: y\r\n\r\n", 1);
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.status, 200);
    }
}
