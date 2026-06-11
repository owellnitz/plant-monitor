//! Minimal MQTT 3.1.1 client: CONNECT + QoS-0 PUBLISH, no auth, no keepalive
//! (keep-alive 0 disables the broker idle timeout; we publish often anyway).

use embedded_io::{Read, Write};

#[derive(Debug)]
pub enum Error {
    Io,
    /// Broker rejected the CONNECT (CONNACK return code != 0).
    ConnectionRefused(u8),
    /// Response was not a valid CONNACK.
    BadResponse,
}

/// Sends CONNECT and waits for CONNACK.
pub fn connect<S: Read + Write>(socket: &mut S, client_id: &str) -> Result<(), Error> {
    let mut packet: heapless::Vec<u8, 64> = heapless::Vec::new();
    packet.push(0x10).unwrap(); // CONNECT
    encode_remaining_length(&mut packet, 10 + 2 + client_id.len());
    // Variable header: protocol name "MQTT", level 4, clean session, keep-alive 0.
    packet
        .extend_from_slice(&[0, 4, b'M', b'Q', b'T', b'T', 4, 0x02, 0, 0])
        .unwrap();
    push_str(&mut packet, client_id);

    socket.write_all(&packet).map_err(|_| Error::Io)?;
    socket.flush().map_err(|_| Error::Io)?;

    let mut connack = [0u8; 4];
    socket.read_exact(&mut connack).map_err(|_| Error::Io)?;
    match connack {
        [0x20, 2, _, 0] => Ok(()),
        [0x20, 2, _, rc] => Err(Error::ConnectionRefused(rc)),
        _ => Err(Error::BadResponse),
    }
}

/// Publishes `payload` to `topic` with QoS 0 (fire and forget, no response).
pub fn publish<S: Write>(socket: &mut S, topic: &str, payload: &[u8]) -> Result<(), Error> {
    let mut packet: heapless::Vec<u8, 256> = heapless::Vec::new();
    packet.push(0x30).unwrap(); // PUBLISH, QoS 0
    encode_remaining_length(&mut packet, 2 + topic.len() + payload.len());
    push_str(&mut packet, topic);
    packet
        .extend_from_slice(payload)
        .expect("MQTT packet buffer too small");

    socket.write_all(&packet).map_err(|_| Error::Io)?;
    socket.flush().map_err(|_| Error::Io)
}

/// MQTT variable-length "remaining length" encoding (7 bits per byte).
fn encode_remaining_length<const N: usize>(packet: &mut heapless::Vec<u8, N>, mut len: usize) {
    loop {
        let mut byte = (len % 128) as u8;
        len /= 128;
        if len > 0 {
            byte |= 0x80;
        }
        packet.push(byte).unwrap();
        if len == 0 {
            break;
        }
    }
}

/// UTF-8 string field: u16 big-endian length prefix + bytes.
fn push_str<const N: usize>(packet: &mut heapless::Vec<u8, N>, s: &str) {
    packet
        .extend_from_slice(&(s.len() as u16).to_be_bytes())
        .unwrap();
    packet
        .extend_from_slice(s.as_bytes())
        .expect("MQTT packet buffer too small");
}

#[cfg(test)]
mod tests {
    use super::*;

    /// In-memory socket: records writes, serves canned read bytes.
    struct MockSocket {
        written: Vec<u8>,
        to_read: Vec<u8>,
        read_pos: usize,
    }

    impl MockSocket {
        fn with_response(to_read: &[u8]) -> Self {
            MockSocket {
                written: Vec::new(),
                to_read: to_read.to_vec(),
                read_pos: 0,
            }
        }
    }

    impl embedded_io::ErrorType for MockSocket {
        type Error = core::convert::Infallible;
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
            let n = remaining.len().min(buf.len());
            buf[..n].copy_from_slice(&remaining[..n]);
            self.read_pos += n;
            Ok(n)
        }
    }

    const CONNACK_OK: &[u8] = &[0x20, 0x02, 0x00, 0x00];

    #[test]
    fn connect_sends_correct_packet() {
        let mut socket = MockSocket::with_response(CONNACK_OK);
        connect(&mut socket, "plant-1").unwrap();

        #[rustfmt::skip]
        let expected = [
            0x10, 19, // CONNECT, remaining length
            0, 4, b'M', b'Q', b'T', b'T', 4, // protocol name + level
            0x02, // flags: clean session
            0, 0, // keep-alive 0
            0, 7, b'p', b'l', b'a', b'n', b't', b'-', b'1', // client id
        ];
        assert_eq!(socket.written, expected);
    }

    #[test]
    fn connect_rejects_refused_connack() {
        // Return code 5 = not authorized.
        let mut socket = MockSocket::with_response(&[0x20, 0x02, 0x00, 0x05]);
        assert!(matches!(
            connect(&mut socket, "x"),
            Err(Error::ConnectionRefused(5))
        ));
    }

    #[test]
    fn connect_rejects_non_connack_response() {
        let mut socket = MockSocket::with_response(&[0x99, 0x02, 0x00, 0x00]);
        assert!(matches!(connect(&mut socket, "x"), Err(Error::BadResponse)));
    }

    #[test]
    fn connect_fails_on_truncated_response() {
        let mut socket = MockSocket::with_response(&[0x20, 0x02]);
        assert!(matches!(connect(&mut socket, "x"), Err(Error::Io)));
    }

    #[test]
    fn publish_sends_correct_packet() {
        let mut socket = MockSocket::with_response(&[]);
        publish(&mut socket, "a/b", b"hi").unwrap();

        #[rustfmt::skip]
        let expected = [
            0x30, 7, // PUBLISH QoS 0, remaining length
            0, 3, b'a', b'/', b'b', // topic
            b'h', b'i', // payload
        ];
        assert_eq!(socket.written, expected);
    }

    #[test]
    fn remaining_length_uses_two_bytes_from_128() {
        // Topic "t" (1 byte) -> remaining length = 2 + 1 + payload.
        let mut socket = MockSocket::with_response(&[]);
        publish(&mut socket, "t", &[0xAA; 124]).unwrap();
        assert_eq!(socket.written[1], 127); // single varint byte

        let mut socket = MockSocket::with_response(&[]);
        publish(&mut socket, "t", &[0xAA; 125]).unwrap();
        assert_eq!(&socket.written[1..3], &[0x80, 0x01]); // 128 -> two bytes
        assert_eq!(socket.written[3..5], [0, 1]); // topic length follows
    }
}
