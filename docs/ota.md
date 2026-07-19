# Over-the-Air (OTA) Firmware Updates

Living reference for the OTA feature, updated as each piece lands. The goal:
flash a device over USB exactly once, then deliver every later firmware update
over WiFi.

**End state**

```
merge a firmware change → release PR → merge
   → CI builds a generic image, attaches it to the GitHub release
   → backend polls GitHub, caches the image in Postgres
   → device (hourly wake) asks the backend "newer than what I run?"
   → downloads to the spare flash slot, verifies, reboots into it
   → reports its new version in every reading
```

The device never talks to GitHub directly (no TLS on the no_std ESP32-C3); the
backend proxies and caches. A failed update never costs more than one wake
cycle — the running firmware is untouched until a fully downloaded image
verifies.

## Status

| Area | Status |
|------|--------|
| Partition layout + firmware build id (this doc's "as built" section) | ✅ done |
| Config partition — WiFi/MQTT from flash, so images become generic | ⬜ planned |
| Backend: store the reported firmware version per reading | ⬜ planned |
| Backend: cache firmware images from GitHub Releases + serve them | ⬜ planned |
| CI: build + attach a generic image to each firmware release | ⬜ planned |
| Frontend: show each sensor's firmware version | ⬜ planned |
| Firmware: HTTP client | ⬜ planned |
| Firmware: OTA core (download → verify → swap slot) | ⬜ planned |
| Firmware: wire OTA into the wake cycle + rollback | ⬜ planned |

## How it works (as built)

### Flash layout

The device runs the ESP-IDF 2nd-stage bootloader with a two-slot OTA layout
(`firmware/partitions.csv`) on the 4 MB flash:

| Partition | Purpose |
|-----------|---------|
| `config` (nvs, 0x9000) | reserved — will hold WiFi/MQTT settings so a generic image runs on any device |
| `otadata` (0xd000) | records which app slot boots |
| `ota_0` (0x10000, ~1.9 MB) | app slot A |
| `ota_1` (0x1f0000, ~1.9 MB) | app slot B |

Two app slots are what OTA needs: an update is written to the *inactive* slot
and only activated once it verifies, so a bad image can't brick the device.
The net image is ~430 KB — over 4× headroom per slot.

### Firmware versioning

`build.rs` bakes a build id into every image via
`git describe --tags --match 'firmware-v*'` (exposed as `config::FW_BUILD`):

- on a firmware release commit → the exact tag, e.g. `firmware-v0.3.0`
- otherwise → `firmware-v0.3.0-<n>-g<hash>` (or `dev` outside a git checkout)

The device reports it as the `fw` field in every MQTT reading:

```
{"id":"a1b2c3d4e5f6","raw":3500,"percent":62,"fw":"firmware-v0.3.0"}
```

The OTA update check will compare this against the latest release tag by string
equality — no version parsing needed.

### Flashing a device

Until OTA is live, USB is the only path. The runner in
`firmware/.cargo/config.toml` flashes the OTA layout via
`--partition-table partitions.csv`, so `cargo run --release --features net`
lays down the two-slot table automatically. See
[firmware/README.md](../firmware/README.md) for wiring and manual-flash
details.

## Planned

Sections below are filled in as the work lands (see the status table):

- **Config provisioning** — moving WiFi/MQTT credentials out of the binary and
  into the `config` partition (one-time cable step per device), which is what
  makes CI-built generic images possible.
- **Backend firmware store** — a hosted worker that polls GitHub Releases and
  caches images in Postgres, plus the endpoints the device polls
  (`/api/firmware/latest`, `/api/firmware/binary`).
- **Release pipeline** — the CI job that builds and attaches the image.
- **Device update flow** — the download/verify/swap cycle and the
  `PendingVerify → Valid` rollback safety net.
