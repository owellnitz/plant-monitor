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

/// WiFi/MQTT/backend settings. Named `Network`, not `Net`, so it doesn't read
/// as the `net` cargo feature.
pub struct Network {
    pub wifi_ssid: heapless::String<32>,
    pub wifi_password: heapless::String<64>,
    pub mqtt_host: heapless::String<40>,
    pub mqtt_port: u16,
    /// Backend HTTP port for the OTA update check. The backend shares the
    /// broker's host, so only the port is configurable.
    pub backend_port: u16,
}

/// Whether a display is fitted. Devices ship both ways — the panel is an
/// option, not part of the board — and the SPI bus to it is write-only, so the
/// firmware cannot find this out for itself. It is provisioned.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Display {
    Oled,
    None,
}

/// Parsed device configuration.
pub struct Config {
    /// `None` when the device was never provisioned for the network, or when
    /// any network key is missing or malformed — the device then reads its
    /// sensor and skips the network.
    pub network: Option<Network>,
    pub display: Display,
}

/// Where the backend listens unless the config says otherwise. Optional on
/// purpose: devices provisioned before OTA existed keep working untouched.
const DEFAULT_BACKEND_PORT: u16 = 5001;

impl Config {
    /// Parses the raw `config` partition bytes. Unknown keys are ignored and
    /// optional ones fall back to a default, so the format can grow without
    /// reprovisioning every device.
    ///
    /// Two kinds of problem, two outcomes:
    ///
    /// - **Structural** — bad magic (an unprovisioned partition reads as
    ///   0xFF..), a bad length, non-UTF-8, a line with no `=`, an unknown
    ///   `display` value. `None`: nothing here can be trusted, and the caller
    ///   falls back to its defaults.
    /// - **A network key** — missing, over-long, or a non-numeric port. The
    ///   config still parses, with `network: None`. A settings-level mistake
    ///   costs the network, not the settings that have nothing to do with it.
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
        let mut backend_port = None;
        let mut display = None;
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
                "mqtt_port" => port = Some(value),
                "backend_port" => backend_port = Some(value),
                "display" => {
                    display = Some(match value {
                        "oled" => Display::Oled,
                        "none" => Display::None,
                        _ => return None,
                    })
                }
                _ => {}
            }
        }

        Some(Config {
            network: parse_network(ssid, password, host, port, backend_port),
            display: display.unwrap_or(Display::Oled),
        })
    }

    /// Reads and parses the `config` partition from flash. `None` on any flash
    /// or partition-table error, or an unprovisioned/invalid partition.
    ///
    /// Chip-only: `esp-storage` is a riscv-target dependency, so the host test
    /// build compiles `parse` but not this.
    #[cfg(target_arch = "riscv32")]
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

/// Assembles the network settings from the keys `parse` collected, or `None`
/// if any of them is missing or unusable. A separate function so its `?`s stop
/// here instead of refusing the whole config.
///
/// `backend_port` is the one optional key: absent it defaults, but a value
/// that won't parse still fails — a typo must not quietly talk to port 5001.
fn parse_network(
    ssid: Option<&str>,
    password: Option<&str>,
    host: Option<&str>,
    port: Option<&str>,
    backend_port: Option<&str>,
) -> Option<Network> {
    Some(Network {
        wifi_ssid: heapless::String::try_from(ssid?).ok()?,
        wifi_password: heapless::String::try_from(password?).ok()?,
        mqtt_host: heapless::String::try_from(host?).ok()?,
        mqtt_port: port?.parse().ok()?,
        backend_port: match backend_port {
            Some(value) => value.parse().ok()?,
            None => DEFAULT_BACKEND_PORT,
        },
    })
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

    /// The network settings a config text yields, for the cases where the
    /// partition itself parses.
    fn network(text: &str) -> Option<Network> {
        Config::parse(&image(text)).unwrap().network
    }

    #[test]
    fn backend_port_defaults_when_absent() {
        // Devices provisioned before OTA existed have no such key and must
        // keep working, so the default stands in.
        assert_eq!(network(VALID).unwrap().backend_port, 5001);
    }

    #[test]
    fn backend_port_is_read_when_present() {
        let text = format!("{VALID}backend_port = \"8080\"\n");
        assert_eq!(network(&text).unwrap().backend_port, 8080);
    }

    #[test]
    fn a_non_numeric_backend_port_drops_the_network() {
        // A typo must not silently fall back to the default and talk to the
        // wrong port; the network is refused, as for mqtt_port.
        let text = format!("{VALID}backend_port = \"http\"\n");
        assert!(network(&text).is_none());
    }

    const VALID: &str = "wifi_ssid = \"home\"\nwifi_password = \"secret\"\nmqtt_host = \"192.168.1.10\"\nmqtt_port = \"1883\"\n";

    #[test]
    fn parses_a_valid_partition() {
        let net = network(VALID).unwrap();
        assert_eq!(net.wifi_ssid, "home");
        assert_eq!(net.wifi_password, "secret");
        assert_eq!(net.mqtt_host, "192.168.1.10");
        assert_eq!(net.mqtt_port, 1883);
    }

    #[test]
    fn ignores_comments_blank_lines_and_unknown_keys() {
        let text = "# device config\n\nwifi_ssid=home\nfuture_key = 5\nwifi_password=secret\n\nmqtt_host=10.0.0.1\nmqtt_port=1883\n";
        let net = network(text).unwrap();
        assert_eq!(net.wifi_ssid, "home");
        assert_eq!(net.mqtt_host, "10.0.0.1");
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
    fn display_defaults_to_oled_when_absent() {
        // Devices provisioned before headless units existed have no such key
        // and must keep driving their panel.
        let cfg = Config::parse(&image(VALID)).unwrap();
        assert_eq!(cfg.display, Display::Oled);
    }

    #[test]
    fn display_none_is_read_when_present() {
        let text = format!("{VALID}display = \"none\"\n");
        let cfg = Config::parse(&image(&text)).unwrap();
        assert_eq!(cfg.display, Display::None);
    }

    #[test]
    fn an_unknown_display_value_rejects_the_config() {
        // A typo must not decide whether the panel is driven; refusing the
        // config falls back to driving it, which is the harmless direction.
        let text = format!("{VALID}display = \"eink\"\n");
        assert!(Config::parse(&image(&text)).is_none());
    }

    #[test]
    fn display_survives_a_broken_network_config() {
        // The panel has nothing to do with WiFi: a device whose network
        // settings are missing must still know it has no screen.
        let cfg = Config::parse(&image("display = \"none\"\n")).unwrap();
        assert!(cfg.network.is_none());
        assert_eq!(cfg.display, Display::None);
    }

    #[test]
    fn a_missing_network_key_drops_the_network() {
        let text = "wifi_ssid=home\nmqtt_host=10.0.0.1\nmqtt_port=1883\n"; // no password
        assert!(network(text).is_none());
    }

    #[test]
    fn a_non_numeric_port_drops_the_network() {
        let text = "wifi_ssid=home\nwifi_password=secret\nmqtt_host=10.0.0.1\nmqtt_port=abc\n";
        assert!(network(text).is_none());
    }

    #[test]
    fn a_value_longer_than_its_field_drops_the_network() {
        let long = "x".repeat(33); // wifi_ssid caps at 32
        let text =
            format!("wifi_ssid={long}\nwifi_password=secret\nmqtt_host=10.0.0.1\nmqtt_port=1883\n");
        assert!(network(&text).is_none());
    }
}
