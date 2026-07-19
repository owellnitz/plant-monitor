//! Build-time configuration, baked in from `config.toml` by build.rs
//! (see config.example.toml).

pub const WIFI_SSID: &str = env!("CFG_WIFI_SSID");
pub const WIFI_PASSWORD: &str = env!("CFG_WIFI_PASSWORD");
pub const MQTT_HOST: &str = env!("CFG_MQTT_HOST");
pub const MQTT_PORT: &str = env!("CFG_MQTT_PORT");

/// Firmware build id from `git describe` (see build.rs). Reported in every
/// reading and compared against the latest release by the OTA update check.
pub const FW_BUILD: &str = env!("CFG_FW_BUILD");
