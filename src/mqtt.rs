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
