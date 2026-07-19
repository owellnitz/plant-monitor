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

## Security disclaimer

This project is designed for a private home network and makes no attempt to
be safe on the open internet:

- The Mosquitto broker accepts **anonymous MQTT connections** (no
  username/password, no TLS) — anyone who can reach port 1883 can publish
  fake readings or subscribe to sensor data.
- The backend API and web app on port 5001 have no authentication.

Do not expose ports 1883 or 5001 beyond your trusted LAN. If you need remote
access, put it behind a VPN.

## Repository layout

| Path | Contents |
|------|----------|
| `firmware/` | ESP32-C3 Rust firmware (sensor, OLED, MQTT) — see [firmware/README.md](firmware/README.md) |
| `backend/` | .NET 10 service (EF Core, controllers → services → repositories): subscribes to the broker, writes readings to Postgres, serves the REST API and the frontend — see [backend/README.md](backend/README.md) |
| `frontend/` | Angular PWA: plant overview, plant detail with 7-day chart, create/edit form, and unassigned-sensor pages (Tailwind + daisyUI, Chart.js) — see [frontend/README.md](frontend/README.md) |
| `mosquitto/` | Mosquitto broker config |
| `docker-compose.yml` | The server stack: Mosquitto on :1883, Postgres, backend + app on :5001 |
| `docker-compose.release.yml` | Overlay that runs the backend from the released GHCR image instead of building locally |
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

This builds the backend image locally. To run the released image from
GHCR instead, layer the release overlay — see
[docs/releasing.md](docs/releasing.md):

```sh
docker compose -f docker-compose.yml -f docker-compose.release.yml up -d
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

The sensor identifies itself by its factory-unique MAC address (12 hex
chars in the MQTT topic) — nothing to configure per device.

### 4. Build and flash

```sh
cargo run --release --features net    # build + flash + serial monitor
```

Toolchain install, wiring, and flashing details: [firmware/README.md](firmware/README.md).

### 5. Verify readings arrive

```sh
mosquitto_sub -h localhost -t 'sensors/#' -v
# sensors/a1b2c3d4e5f6/moisture {"id":"a1b2c3d4e5f6","raw":3500,"percent":62,"fw":"firmware-v0.3.0"}
```

The backend stores at most one reading per device per 5 minutes (repeats
within that window are replays from an unexpected device reboot and get
dropped); check the database directly:

```sh
docker compose exec db psql -U plantmonitor -c 'SELECT * FROM readings;'
```

### No hardware? Publish a test reading

Any MQTT client can stand in for the sensor. With the stack running:

```sh
mosquitto_pub -h localhost -t 'sensors/sensor-001/moisture' \
  -m '{"id":"sensor-001","raw":2376,"percent":58}'
```

The device appears in the UI as an unassigned sensor with one reading;
assign it to a plant to test the binding flow. Message format and how to
publish a whole series of readings: [docs/sample-data.md](docs/sample-data.md).

The firmware publishes once per hour (deep sleep in between); tap RST on the
devkit to force an immediate reading — note it is only stored if the last
one is more than 5 minutes old.

### 6. View readings in the app

Open [http://localhost:5001](http://localhost:5001) — the Angular PWA shows
one card per plant. A newly reporting sensor appears under **New sensors**
until you create a plant for it (name, species, location, sun exposure) and
bind the sensor. Each plant's detail page has its latest reading, a 7-day
chart and the most recent readings, with edit/delete. Installable from the
browser (service worker requires localhost or HTTPS).

## Releases

Two components are versioned independently from Conventional Commits on
`main`: `app` (backend + frontend, tagged `app-vX.Y.Z`, published to GHCR
as `:X.Y.Z` and `:latest`) and `firmware` (tagged `firmware-vX.Y.Z`).
release-please maintains one release PR per component; merging that PR cuts
the release. Details: [docs/releasing.md](docs/releasing.md).

Firmware updates are moving from USB to over-the-air delivery (flash once,
update over WiFi thereafter) — progress and design in [docs/ota.md](docs/ota.md).
