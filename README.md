# plant-monitor

Home plant monitoring. An ESP32-C3 with a soil moisture sensor shows the
reading on an OLED and (with the `net` feature) publishes it hourly over MQTT
to a Mosquitto broker.

```
Firmware (ESP32-C3, Rust)
   │  MQTT (sensors/<device_id>/moisture)
   ▼
Mosquitto broker (Docker)
```

## Repository layout

| Path | Contents |
|------|----------|
| `firmware/` | ESP32-C3 Rust firmware (sensor, OLED, MQTT) — see [firmware/README.md](firmware/README.md) |
| `mosquitto/` | Mosquitto broker config |
| `docker-compose.yml` | The server stack: Mosquitto on :1883 |
| `.github/` | CI + dependabot |

Root level holds only what spans the whole system; each component lives in its
own directory with its own README and tooling.

## Setup

End-to-end: broker first, then point the firmware at it.

### 1. Start the MQTT broker

```sh
docker compose up -d        # Mosquitto on :1883
```

The broker allows anonymous connections (`mosquitto/mosquitto.conf`) — no
username/password. Trusted local network only.

### 2. Find the broker's LAN IP

The firmware needs the broker as an IPv4 address (no DNS). On macOS:

```sh
ipconfig getifaddr en0
```

### 3. Configure the firmware

WiFi and MQTT settings are baked in at build time from
`firmware/config.toml` (gitignored — it holds the WiFi password):

```sh
cd firmware
cp config.example.toml config.toml
```

Then edit `config.toml`:

| Key | Value |
|-----|-------|
| `wifi_ssid` | Your 2.4 GHz WiFi name (the ESP32-C3 has no 5 GHz) |
| `wifi_password` | Your WiFi password |
| `mqtt_host` | Broker LAN IP from step 2 |
| `mqtt_port` | `1883` |
| `device_id` | Name for this sensor, e.g. `plant-1` (becomes the MQTT topic) |

### 4. Build and flash

```sh
cargo run --release --features net    # build + flash + serial monitor
```

Toolchain install, wiring, and flashing details: [firmware/README.md](firmware/README.md).

### 5. Verify readings arrive

```sh
mosquitto_sub -h localhost -t 'sensors/#' -v
# sensors/plant-1/moisture {"id":"plant-1","raw":3500,"percent":62}
```

The firmware publishes once per hour (deep sleep in between); tap RST on the
devkit to force an immediate reading.
