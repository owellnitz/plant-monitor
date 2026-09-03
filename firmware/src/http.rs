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
    /// The body did not fit in the caller's buffer.
    BodyTooLarge,
    /// The response headers did not arrive within the timeout.
    Timeout,
}

/// A response with its headers parsed. The body is not buffered: the device
/// streams a ~440 KB image straight to flash, so `body` holds only the bytes
/// that happened to arrive alongside the headers and the caller reads the rest
/// from the socket itself.
pub struct Response<'a> {
    pub status: u16,
    /// `Content-Length`, absent when the server did not send one.
    pub content_length: Option<u32>,
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
    let content_length = parse_content_length(&buf[..header_end]);

    Ok(Response {
        status,
        content_length,
        body: &buf[header_end..filled],
    })
}

/// Reads a short response body into `buf` and returns it.
///
/// [`get`] only returns the body bytes that shared a packet with the headers,
/// so the rest is pulled from the socket here.
///
/// `len` is the `Content-Length` when the server sent one. **It usually has
/// not**: HTTP/1.0 lets a server delimit the body by closing the connection,
/// and that is exactly what our backend does for the update check. With no
/// length, this reads until the peer hangs up.
///
/// Large bodies are streamed instead (see `crate::ota::download`); the image
/// endpoint does send a Content-Length.
pub fn read_body<'a, S: Read + ReadReady>(
    socket: &mut S,
    prefix: &[u8],
    len: Option<usize>,
    buf: &'a mut [u8],
    now_ms: impl Fn() -> u64,
    timeout_ms: u64,
) -> Result<&'a [u8], Error> {
    // Either an announced length or the prefix alone can overrun the buffer.
    if len.is_some_and(|len| len > buf.len()) || prefix.len() > buf.len() {
        return Err(Error::BodyTooLarge);
    }

    let want = len.unwrap_or(buf.len());
    let have = prefix.len().min(want);
    buf[..have].copy_from_slice(&prefix[..have]);

    let deadline = now_ms() + timeout_ms;
    let mut filled = have;

    loop {
        match len {
            Some(len) if filled >= len => return Ok(&buf[..len]),
            // Without a length the body ends at the close, so a full buffer
            // means the answer was bigger than the caller can hold.
            None if filled == buf.len() => return Err(Error::BodyTooLarge),
            _ => {}
        }

        match socket.read_ready() {
            Ok(true) => match socket.read(&mut buf[filled..want]) {
                // A close-delimited body ends here.
                Ok(0) => return finish(buf, filled, len),
                Ok(n) => filled += n,
                // The network stack reports a hung-up peer as an error rather
                // than a zero-length read, so that is an ending too.
                Err(_) => return finish(buf, filled, len),
            },
            Ok(false) if now_ms() >= deadline => return Err(Error::Timeout),
            Ok(false) => {}
            Err(_) => return finish(buf, filled, len),
        }
    }
}

/// The connection ended. That completes a close-delimited body, but truncates
/// one whose length the server promised.
fn finish(buf: &[u8], filled: usize, len: Option<usize>) -> Result<&[u8], Error> {
    match len {
        // Nothing at all arrived: the connection failed rather than delimited.
        None if filled > 0 => Ok(&buf[..filled]),
        _ => Err(Error::BadResponse),
    }
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

/// `Content-Length` if present and parsable. Header names are case-insensitive,
/// and a missing or unparsable value is simply absent rather than an error —
/// the caller decides whether it needed one.
fn parse_content_length(headers: &[u8]) -> Option<u32> {
    const NAME: &[u8] = b"content-length:";

    headers
        .split(|&b| b == b'\n')
        .find(|line| {
            line.len() > NAME.len()
                && line[..NAME.len()]
                    .iter()
                    .zip(NAME)
                    .all(|(b, n)| b.to_ascii_lowercase() == *n)
        })
        .and_then(|line| {
            let mut digits = line[NAME.len()..]
                .iter()
                .copied()
                .skip_while(|b| *b == b' ')
                .take_while(u8::is_ascii_digit)
                .peekable();
            // An empty or non-numeric value must not read as zero.
            digits.peek()?;
            digits.try_fold(0u32, |acc, b| {
                acc.checked_mul(10)?.checked_add(u32::from(b - b'0'))
            })
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
        /// Once drained, still report readable and read 0 bytes.
        closes: bool,
        /// Once drained, fail read_ready — how blocking-network-stack actually
        /// reports a peer that hung up (IoError::SocketClosed).
        errors_on_close: bool,
    }

    impl MockSocket {
        fn with_response(to_read: &[u8]) -> Self {
            MockSocket {
                written: Vec::new(),
                to_read: to_read.to_vec(),
                read_pos: 0,
                chunk: usize::MAX,
                closes: false,
                errors_on_close: false,
            }
        }

        /// Delivers at most `chunk` bytes per read.
        fn in_chunks(to_read: &[u8], chunk: usize) -> Self {
            MockSocket {
                chunk,
                ..MockSocket::with_response(to_read)
            }
        }

        /// Serves `to_read`, then signals end-of-stream instead of going quiet.
        fn then_closes(to_read: &[u8]) -> Self {
            MockSocket {
                closes: true,
                ..MockSocket::with_response(to_read)
            }
        }

        /// Serves `to_read`, then errors the way the real socket does when the
        /// peer has hung up.
        fn then_errors(to_read: &[u8]) -> Self {
            MockSocket {
                errors_on_close: true,
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
            if self.errors_on_close && self.read_pos >= self.to_read.len() {
                return Err(embedded_io::ErrorKind::BrokenPipe);
            }
            Ok(self.closes || self.read_pos < self.to_read.len())
        }
    }

    /// Clock that never advances — for responses already buffered, where the
    /// deadline is never consulted.
    fn frozen_clock() -> impl Fn() -> u64 {
        || 0
    }

    /// Clock that jumps a full second per call, so any wait on a silent socket
    /// trips a 1000 ms timeout immediately.
    fn ticking_clock() -> impl Fn() -> u64 {
        let t = core::cell::Cell::new(0u64);
        move || {
            let v = t.get();
            t.set(v + 1000);
            v
        }
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

    #[test]
    fn get_reads_the_content_length() {
        let mut socket =
            MockSocket::with_response(b"HTTP/1.1 200 OK\r\nContent-Length: 441264\r\n\r\n");
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.content_length, Some(441264));
    }

    // Header names are case-insensitive, and servers differ on the spelling.
    #[test]
    fn get_reads_a_lowercase_content_length() {
        let mut socket = MockSocket::with_response(b"HTTP/1.1 200 OK\r\ncontent-length:7\r\n\r\n");
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.content_length, Some(7));
    }

    #[test]
    fn get_reports_no_content_length_when_absent_or_unparsable() {
        for headers in [
            b"HTTP/1.1 204 No Content\r\n\r\n".as_slice(),
            b"HTTP/1.1 200 OK\r\nContent-Length: abc\r\n\r\n",
            // A length that overflows u32 is no more usable than a missing one.
            b"HTTP/1.1 200 OK\r\nContent-Length: 99999999999\r\n\r\n",
        ] {
            let mut socket = MockSocket::with_response(headers);
            let mut buf = [0u8; 256];

            let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

            assert_eq!(response.content_length, None, "headers: {headers:?}");
        }
    }

    // A header line must not be confused with one whose name merely ends the
    // same way.
    #[test]
    fn get_ignores_a_similarly_named_header() {
        let mut socket =
            MockSocket::with_response(b"HTTP/1.1 200 OK\r\nX-Content-Length: 5\r\n\r\n");
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.content_length, None);
    }

    // Body bytes arriving in the same packet as the headers must be handed
    // back, not dropped — the caller reads only the remainder from the socket.
    #[test]
    fn get_returns_body_bytes_that_arrived_with_the_headers() {
        let mut socket =
            MockSocket::with_response(b"HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        let mut buf = [0u8; 256];

        let response = get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(response.body, b"hello");
        assert_eq!(response.content_length, Some(5));
    }

    // The device is unattended: a broker or backend that accepts the
    // connection and then goes quiet must not hold it awake.
    /// The exact bytes Kestrel returns to this client's HTTP/1.0 request for
    /// the update check — captured from the running backend. Note there is no
    /// Content-Length: the body is delimited by the connection closing.
    const KESTREL_UPDATE_CHECK: &[u8] = b"HTTP/1.1 200 OK\r\n\
        Connection: close\r\n\
        Content-Type: application/json; charset=utf-8\r\n\
        Date: Thu, 03 Sep 2026 21:56:30 GMT\r\n\
        Server: Kestrel\r\n\
        \r\n\
        {\"version\":\"firmware-v0.5.0\",\"size\":457200,\"sha256\":\"dc35d9b3fdd25c04cb661c5572c74b70966ad19585819f52fb682af1e719a3fa\"}";

    // The regression that shipped: the update check has no Content-Length, so
    // requiring one meant every device silently skipped every update.
    #[test]
    fn reads_the_real_backend_update_check() {
        let mut socket = MockSocket::then_errors(KESTREL_UPDATE_CHECK);
        let mut header_buf = [0u8; 512];
        let mut body_buf = [0u8; 256];

        let response = get(
            &mut socket,
            "backend",
            "/api/firmware/latest?current=firmware-v0.4.0-15-gec8cb6e",
            &mut header_buf,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(response.status, 200);
        assert_eq!(response.content_length, None, "backend sends no length");

        let body = read_body(
            &mut socket,
            response.body,
            response.content_length.map(|len| len as usize),
            &mut body_buf,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(
            body,
            br#"{"version":"firmware-v0.5.0","size":457200,"sha256":"dc35d9b3fdd25c04cb661c5572c74b70966ad19585819f52fb682af1e719a3fa"}"#
        );
    }

    // The real socket reports a hung-up peer as an error from read_ready, not
    // as a readable zero-length read, so both have to end the body.
    #[test]
    fn read_body_without_a_length_ends_at_the_close() {
        for mut socket in [
            MockSocket::then_closes(b"{\"a\":1}"),
            MockSocket::then_errors(b"{\"a\":1}"),
        ] {
            let mut buf = [0u8; 64];

            let body = read_body(&mut socket, b"", None, &mut buf, frozen_clock(), 1000).unwrap();

            assert_eq!(body, br#"{"a":1}"#);
        }
    }

    // Everything may already have arrived with the headers, leaving the socket
    // closed before the first read.
    #[test]
    fn read_body_without_a_length_accepts_a_complete_prefix() {
        let mut socket = MockSocket::then_errors(b"");
        let mut buf = [0u8; 64];

        let body = read_body(&mut socket, b"{}", None, &mut buf, frozen_clock(), 1000).unwrap();

        assert_eq!(body, b"{}");
    }

    #[test]
    fn read_body_without_a_length_rejects_a_body_past_the_buffer() {
        let mut socket = MockSocket::then_closes(b"0123456789");
        let mut buf = [0u8; 4];

        assert!(matches!(
            read_body(&mut socket, b"", None, &mut buf, frozen_clock(), 1000),
            Err(Error::BodyTooLarge)
        ));
    }

    #[test]
    fn read_body_returns_a_body_that_came_with_the_headers() {
        let mut socket = MockSocket::with_response(b"");
        let mut buf = [0u8; 64];

        let body = read_body(
            &mut socket,
            b"hello",
            Some(5),
            &mut buf,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(body, b"hello");
    }

    // The interesting case: the backend split the response, so the rest of the
    // JSON is still on the socket.
    #[test]
    fn read_body_pulls_the_remainder_from_the_socket() {
        let mut socket = MockSocket::in_chunks(b" world", 2);
        let mut buf = [0u8; 64];

        let body = read_body(
            &mut socket,
            b"hello",
            Some(11),
            &mut buf,
            frozen_clock(),
            1000,
        )
        .unwrap();

        assert_eq!(body, b"hello world");
    }

    // Content-Length may overstate what the server actually sends.
    #[test]
    fn read_body_rejects_a_short_body() {
        let mut socket = MockSocket::then_closes(b"ab");
        let mut buf = [0u8; 64];

        assert!(matches!(
            read_body(&mut socket, b"", Some(10), &mut buf, frozen_clock(), 1000),
            Err(Error::BadResponse)
        ));
    }

    #[test]
    fn read_body_rejects_a_body_larger_than_the_buffer() {
        let mut socket = MockSocket::with_response(b"");
        let mut buf = [0u8; 8];

        assert!(matches!(
            read_body(&mut socket, b"", Some(9), &mut buf, frozen_clock(), 1000),
            Err(Error::BodyTooLarge)
        ));
    }

    #[test]
    fn read_body_times_out_on_a_stalled_body() {
        let mut socket = MockSocket::with_response(b"ab");
        let mut buf = [0u8; 64];

        assert!(matches!(
            read_body(&mut socket, b"", Some(10), &mut buf, ticking_clock(), 1000),
            Err(Error::Timeout)
        ));
    }

    #[test]
    fn get_times_out_on_headers_that_never_finish() {
        let mut socket = MockSocket::with_response(b"HTTP/1.1 200 OK\r\nContent-Length: 5\r\n");
        let mut buf = [0u8; 256];

        assert!(matches!(
            get(&mut socket, "h", "/", &mut buf, ticking_clock(), 1000),
            Err(Error::Timeout)
        ));
    }

    // HTTP/1.0 closes to signal the end; before the blank line that means the
    // response was truncated, not that it is still coming.
    #[test]
    fn get_rejects_a_connection_closed_mid_headers() {
        let mut socket = MockSocket::then_closes(b"HTTP/1.1 200 OK\r\n");
        let mut buf = [0u8; 256];

        assert!(matches!(
            get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000),
            Err(Error::BadResponse)
        ));
    }

    #[test]
    fn get_rejects_headers_larger_than_the_buffer() {
        let mut response = b"HTTP/1.1 200 OK\r\nX: ".to_vec();
        response.extend(core::iter::repeat_n(b'y', 200));
        response.extend_from_slice(b"\r\n\r\n");
        let mut socket = MockSocket::with_response(&response);
        let mut buf = [0u8; 64];

        assert!(matches!(
            get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000),
            Err(Error::HeadersTooLarge)
        ));
    }

    #[test]
    fn get_rejects_a_malformed_status_line() {
        for headers in [
            b"HTTP/1.1 2x0 Weird\r\n\r\n".as_slice(),
            b"HTTP/1.1 20 Short\r\n\r\n",
            b"NOT-HTTP 200 OK\r\n\r\n",
            b"HTTP/1.1\r\n\r\n",
        ] {
            let mut socket = MockSocket::with_response(headers);
            let mut buf = [0u8; 256];

            assert!(
                matches!(
                    get(&mut socket, "h", "/", &mut buf, frozen_clock(), 1000),
                    Err(Error::BadResponse)
                ),
                "headers: {headers:?}"
            );
        }
    }
}
