//! Device configuration.
//!
//! WiFi/MQTT settings live in the `config` flash partition (provisioned once
//! per device over USB — see provision.sh), so a single generic image runs on
//! any device and survives OTA updates. The build id stays compile-time.
//!
//! Partition layout: a 4-byte magic, a little-endian `u32` payload length,
//! then the config text (the same `key = "value"` lines as config.toml).

/// Firmware build id from `git describe` (see build.rs). Reported in every
/// reading and compared against the latest release by the OTA update check.
pub const FW_BUILD: &str = env!("CFG_FW_BUILD");

/// Marks a provisioned config partition (erased flash reads as 0xFF..).
const MAGIC: [u8; 4] = *b"PMC1";

/// Upper bound on the config text; also caps how much unparsed flash we trust.
const MAX_PAYLOAD: usize = 1024;

/// Parsed device configuration.
pub struct Config {
    pub wifi_ssid: heapless::String<32>,
    pub wifi_password: heapless::String<64>,
    pub mqtt_host: heapless::String<40>,
    pub mqtt_port: u16,
}

impl Config {
    /// Parses the raw `config` partition bytes. Returns `None` when the
    /// partition is unprovisioned (bad magic), corrupt, or missing a required
    /// key — the caller then skips the network path. Unknown keys are ignored
    /// so the format can grow (e.g. a future `backend_port`) without a
    /// reprovision.
    pub fn parse(raw: &[u8]) -> Option<Config> {
        if raw.len() < 8 || raw[0..4] != MAGIC {
            return None;
        }
        let len = u32::from_le_bytes([raw[4], raw[5], raw[6], raw[7]]) as usize;
        if len == 0 || len > MAX_PAYLOAD || 8 + len > raw.len() {
            return None;
        }
        let text = core::str::from_utf8(&raw[8..8 + len]).ok()?;

        let mut ssid = None;
        let mut password = None;
        let mut host = None;
        let mut port = None;
        for line in text.lines() {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            let (key, value) = line.split_once('=')?;
            let value = value.trim().trim_matches('"');
            match key.trim() {
                "wifi_ssid" => ssid = Some(value),
                "wifi_password" => password = Some(value),
                "mqtt_host" => host = Some(value),
                "mqtt_port" => port = Some(value.parse::<u16>().ok()?),
                _ => {}
            }
        }

        Some(Config {
            wifi_ssid: heapless::String::try_from(ssid?).ok()?,
            wifi_password: heapless::String::try_from(password?).ok()?,
            mqtt_host: heapless::String::try_from(host?).ok()?,
            mqtt_port: port?,
        })
    }

    /// Reads and parses the `config` partition from flash. `None` on any flash
    /// or partition-table error, or an unprovisioned/invalid partition.
    #[cfg(feature = "net")]
    pub fn load(flash: &mut esp_storage::FlashStorage<'_>) -> Option<Config> {
        use embedded_storage::ReadStorage;
        use esp_bootloader_esp_idf::partitions::{
            DataPartitionSubType, PARTITION_TABLE_MAX_LEN, PartitionType, read_partition_table,
        };

        let mut table_buf = [0u8; PARTITION_TABLE_MAX_LEN];
        let table = read_partition_table(flash, &mut table_buf).ok()?;
        let entry = table
            .find_partition(PartitionType::Data(DataPartitionSubType::Nvs))
            .ok()??;

        let mut region = entry.as_embedded_storage(flash);
        let mut buf = [0u8; MAX_PAYLOAD + 8];
        region.read(0, &mut buf).ok()?;
        Config::parse(&buf)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Builds a raw config partition image: magic + LE length + text, then
    /// erased-flash 0xFF padding as the real partition would have.
    fn image(text: &str) -> Vec<u8> {
        let mut v = Vec::new();
        v.extend_from_slice(&MAGIC);
        v.extend_from_slice(&(text.len() as u32).to_le_bytes());
        v.extend_from_slice(text.as_bytes());
        v.resize(v.len() + 64, 0xFF);
        v
    }

    const VALID: &str = "wifi_ssid = \"home\"\nwifi_password = \"secret\"\nmqtt_host = \"192.168.1.10\"\nmqtt_port = \"1883\"\n";

    #[test]
    fn parses_a_valid_partition() {
        let cfg = Config::parse(&image(VALID)).unwrap();
        assert_eq!(cfg.wifi_ssid, "home");
        assert_eq!(cfg.wifi_password, "secret");
        assert_eq!(cfg.mqtt_host, "192.168.1.10");
        assert_eq!(cfg.mqtt_port, 1883);
    }

    #[test]
    fn ignores_comments_blank_lines_and_unknown_keys() {
        let text = "# device config\n\nwifi_ssid=home\nfuture_key = 5\nwifi_password=secret\n\nmqtt_host=10.0.0.1\nmqtt_port=1883\n";
        let cfg = Config::parse(&image(text)).unwrap();
        assert_eq!(cfg.wifi_ssid, "home");
        assert_eq!(cfg.mqtt_host, "10.0.0.1");
    }

    #[test]
    fn rejects_bad_magic() {
        let mut img = image(VALID);
        img[0] = b'X';
        assert!(Config::parse(&img).is_none());
    }

    #[test]
    fn rejects_length_past_end_of_buffer() {
        let mut img = image(VALID);
        img[4] = 0xFF; // claim a payload far larger than the buffer
        assert!(Config::parse(&img).is_none());
    }

    #[test]
    fn rejects_missing_required_key() {
        let text = "wifi_ssid=home\nmqtt_host=10.0.0.1\nmqtt_port=1883\n"; // no password
        assert!(Config::parse(&image(text)).is_none());
    }

    #[test]
    fn rejects_non_numeric_port() {
        let text = "wifi_ssid=home\nwifi_password=secret\nmqtt_host=10.0.0.1\nmqtt_port=abc\n";
        assert!(Config::parse(&image(text)).is_none());
    }

    #[test]
    fn rejects_value_longer_than_field() {
        let long = "x".repeat(33); // wifi_ssid caps at 32
        let text =
            format!("wifi_ssid={long}\nwifi_password=secret\nmqtt_host=10.0.0.1\nmqtt_port=1883\n");
        assert!(Config::parse(&image(&text)).is_none());
    }
}
