# plant-monitor

[![Backend](https://github.com/owellnitz/plant-monitor/actions/workflows/backend.yml/badge.svg)](https://github.com/owellnitz/plant-monitor/actions/workflows/backend.yml)
[![Firmware](https://github.com/owellnitz/plant-monitor/actions/workflows/firmware.yml/badge.svg)](https://github.com/owellnitz/plant-monitor/actions/workflows/firmware.yml)
[![Frontend](https://github.com/owellnitz/plant-monitor/actions/workflows/frontend.yml/badge.svg)](https://github.com/owellnitz/plant-monitor/actions/workflows/frontend.yml)

Home plant monitoring. An ESP32-C3 with a soil moisture sensor shows the
reading on an OLED and (with the `net` feature) publishes it hourly over MQTT
to a Mosquitto broker.

```
Firmware (ESP32-C3, Rust)
   │  MQTT (sensors/<device_id>/moisture)
   ▼
Mosquitto broker ──► .NET backend ──► Postgres
                          │
                          ▼  REST API + Angular PWA on :5001
                      Browser
        (all Docker, via docker-compose.yml)
```

## Repository layout

| Path | Contents |
|------|----------|
| `firmware/` | ESP32-C3 Rust firmware (sensor, OLED, MQTT) — see [firmware/README.md](firmware/README.md) |
| `backend/` | .NET 10 service: subscribes to the broker, writes readings to Postgres, serves the REST API and the frontend |
| `frontend/` | Angular PWA: sensor select page + per-sensor detail with 7-day chart (Tailwind + daisyUI, Chart.js) |
| `mosquitto/` | Mosquitto broker config |
| `docker-compose.yml` | The server stack: Mosquitto on :1883, Postgres, backend + app on :5001 |
| `.github/` | CI + dependabot |

Root level holds only what spans the whole system; each component lives in its
own directory with its own README and tooling.

## Setup

End-to-end: broker first, then point the firmware at it.

### 1. Start the server stack

Postgres credentials come from `.env` (gitignored):

```sh
cp .env.example .env        # then set POSTGRES_PASSWORD to a real value
docker compose up -d        # Mosquitto :1883, Postgres :5432, backend + app :5001
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

The backend stores every reading; check the database directly:

```sh
docker compose exec db psql -U plantmonitor -c 'SELECT * FROM readings;'
```

The firmware publishes once per hour (deep sleep in between); tap RST on the
devkit to force an immediate reading.

### 6. View readings in the app

Open [http://localhost:5001](http://localhost:5001) — the Angular PWA shows
one card per sensor; tap a sensor for its latest reading, a 7-day chart and
the most recent readings. Installable from the browser (service worker
requires localhost or HTTPS).
