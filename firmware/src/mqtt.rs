//! Minimal MQTT 3.1.1 client: CONNECT + QoS-0 PUBLISH, no auth, no keepalive
//! (keep-alive 0 disables the broker idle timeout; we publish often anyway).

use embedded_io::{Read, ReadReady, Write};

#[derive(Debug)]
pub enum Error {
    Io,
    /// Broker rejected the CONNECT (CONNACK return code != 0).
    ConnectionRefused(u8),
    /// Response was not a valid CONNACK.
    BadResponse,
    /// No CONNACK arrived within the timeout.
    Timeout,
}

/// Sends CONNECT and waits for CONNACK, giving up after `timeout_ms`.
///
/// The blocking socket's `read` spins forever when no bytes are buffered, so a
/// lost or delayed CONNACK would otherwise wedge the device (connected at the
/// broker but never publishing or sleeping). We poll `read_ready` against a
/// `now_ms` deadline and only read bytes that are already available.
pub fn connect<S: Read + Write + ReadReady>(
    socket: &mut S,
    client_id: &str,
    now_ms: impl Fn() -> u64,
    timeout_ms: u64,
) -> Result<(), Error> {
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

    let deadline = now_ms() + timeout_ms;
    let mut connack = [0u8; 4];
    let mut got = 0;
    while got < connack.len() {
        match socket.read_ready() {
            // Bytes are buffered, so this read won't block.
            Ok(true) => got += socket.read(&mut connack[got..]).map_err(|_| Error::Io)?,
            Ok(false) if now_ms() >= deadline => return Err(Error::Timeout),
            Ok(false) => {}
            Err(_) => return Err(Error::Io),
        }
    }
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

/// One reading to publish: the CONNECT client id plus the PUBLISH topic and
/// payload, all constant for a given wake cycle.
pub struct Message<'a> {
    pub client_id: &'a str,
    pub topic: &'a str,
    pub payload: &'a [u8],
}

/// Publishes one reading over a fresh TCP connection: open, CONNECT, QoS-0
/// PUBLISH, then tear down. No retry: a QoS-0 resend can duplicate a reading
/// that already reached the broker, so a failed publish just waits for the
/// next wake cycle (lost segments are covered by TCP retransmit during the
/// tx-drain before teardown). Any failure gives up quietly — an unreachable
/// broker must never hang or panic the device. `open` and `disconnect` wrap
/// the platform socket's TCP teardown/setup so this flow is testable on the
/// host. `now_ms`/`connack_timeout_ms` bound the CONNACK wait (see `connect`).
pub fn publish_cycle<S: Read + Write + ReadReady>(
    socket: &mut S,
    mut open: impl FnMut(&mut S) -> bool,
    mut disconnect: impl FnMut(&mut S),
    msg: &Message,
    now_ms: impl Fn() -> u64,
    connack_timeout_ms: u64,
) {
    if open(socket) && connect(socket, msg.client_id, &now_ms, connack_timeout_ms).is_ok() {
        let _ = publish(socket, msg.topic, msg.payload);
    }
    disconnect(socket);
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
            let n = remaining.len().min(buf.len());
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

    const CONNACK_OK: &[u8] = &[0x20, 0x02, 0x00, 0x00];

    /// Clock that never advances — for paths where the CONNACK is already
    /// buffered, so the deadline is never consulted.
    fn frozen_clock() -> impl Fn() -> u64 {
        || 0
    }

    /// Clock that jumps a full second per call, so any wait for an absent
    /// CONNACK trips a 1000 ms timeout immediately.
    fn ticking_clock() -> impl Fn() -> u64 {
        let t = core::cell::Cell::new(0u64);
        move || {
            let v = t.get();
            t.set(v + 1000);
            v
        }
    }

    #[test]
    fn connect_sends_correct_packet() {
        let mut socket = MockSocket::with_response(CONNACK_OK);
        connect(&mut socket, "plant-1", frozen_clock(), 1000).unwrap();

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
            connect(&mut socket, "x", frozen_clock(), 1000),
            Err(Error::ConnectionRefused(5))
        ));
    }

    #[test]
    fn connect_rejects_non_connack_response() {
        let mut socket = MockSocket::with_response(&[0x99, 0x02, 0x00, 0x00]);
        assert!(matches!(
            connect(&mut socket, "x", frozen_clock(), 1000),
            Err(Error::BadResponse)
        ));
    }

    #[test]
    fn connect_times_out_on_truncated_response() {
        // Only 2 of the 4 CONNACK bytes ever arrive; the wait must not hang.
        let mut socket = MockSocket::with_response(&[0x20, 0x02]);
        assert!(matches!(
            connect(&mut socket, "x", ticking_clock(), 1000),
            Err(Error::Timeout)
        ));
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

    // CONNECT for client id "x": 1 fixed header + 1 remaining length
    // + 10 variable header + 3 client id field.
    const CONNECT_LEN: usize = 15;

    #[test]
    fn publish_cycle_gives_up_when_broker_unreachable() {
        let mut socket = MockSocket::with_response(&[]);
        let mut opens = 0;
        let mut disconnects = 0;
        publish_cycle(
            &mut socket,
            |_| {
                opens += 1;
                false
            },
            |_| disconnects += 1,
            &Message {
                client_id: "x",
                topic: "t",
                payload: b"p",
            },
            frozen_clock(),
            1000,
        );
        assert_eq!(opens, 1); // retry is only for publish failures
        assert_eq!(disconnects, 1); // final teardown
        assert!(socket.written.is_empty());
    }

    #[test]
    fn publish_cycle_gives_up_when_connect_refused() {
        // Return code 5 = not authorized.
        let mut socket = MockSocket::with_response(&[0x20, 0x02, 0x00, 0x05]);
        publish_cycle(
            &mut socket,
            |_| true,
            |_| {},
            &Message {
                client_id: "x",
                topic: "t",
                payload: b"p",
            },
            frozen_clock(),
            1000,
        );
        // CONNECT went out, no PUBLISH after it.
        assert_eq!(socket.written[0], 0x10);
        assert_eq!(socket.written.len(), CONNECT_LEN);
    }

    #[test]
    fn publish_cycle_gives_up_when_broker_sends_no_connack() {
        // TCP opened but the broker died before answering: the CONNACK wait
        // must time out rather than spin forever.
        let mut socket = MockSocket::with_response(&[]);
        publish_cycle(
            &mut socket,
            |_| true,
            |_| {},
            &Message {
                client_id: "x",
                topic: "t",
                payload: b"p",
            },
            ticking_clock(),
            1000,
        );
        assert_eq!(socket.written.len(), CONNECT_LEN);
    }

    #[test]
    fn publish_cycle_sends_connect_then_publish() {
        let mut socket = MockSocket::with_response(CONNACK_OK);
        let mut disconnects = 0;
        publish_cycle(
            &mut socket,
            |_| true,
            |_| disconnects += 1,
            &Message {
                client_id: "x",
                topic: "t",
                payload: b"p",
            },
            frozen_clock(),
            1000,
        );
        assert_eq!(disconnects, 1);
        assert_eq!(socket.written[0], 0x10);
        assert_eq!(socket.written[CONNECT_LEN], 0x30);
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
