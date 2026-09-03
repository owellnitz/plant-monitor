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
| Config partition — WiFi/MQTT from flash, so images become generic | ✅ done |
| Backend: store the reported firmware version per reading | ✅ done |
| Frontend: show each sensor's firmware version | ✅ done |
| CI: build + attach a generic image to each firmware release | ✅ done |
| Backend: cache firmware images from GitHub Releases + serve them | ✅ done |
| Firmware: HTTP client | ✅ done |
| Firmware: OTA core (download → verify → swap slot) | ✅ done |
| Firmware: wire OTA into the wake cycle + rollback | ✅ done |

## How it works (as built)

### Flash layout

The device runs the ESP-IDF 2nd-stage bootloader with a two-slot OTA layout
(`firmware/partitions.csv`) on the 4 MB flash:

| Partition | Purpose |
|-----------|---------|
| `config` (nvs, 0x9000) | WiFi/MQTT settings, so a generic image runs on any device (see Configuration below) |
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
{"id":"a1b2c3d4e5f6","raw":3500,"percent":62,"fw":"firmware-v0.3.0","reset":"deep_sleep"}
```

The OTA update check will compare this against the latest release tag by string
equality — no version parsing needed.

The backend stores the reported version on every reading and serves it per
sensor; the frontend shows it on the sensor list and detail pages. Until OTA
lands that is the only way to see what a device is actually running — and
afterwards it is how an update is confirmed to have taken.

### Configuration (generic images)

WiFi/MQTT settings live in the `config` flash partition, not the binary, so one
image runs on any device. The partition holds a small framed blob — magic
`PMC1`, a little-endian length, then the `key = "value"` config text — read and
parsed at boot (`firmware/src/config.rs`). A missing or invalid partition means
the device shows the reading but skips the network; no build failure, no panic.

Provision a device once over USB with `firmware/provision.sh` (config survives
OTA updates, so this is a one-time step unless the WiFi settings change). The
build no longer reads `config.toml` — that's what makes CI-built release images
possible.

### Flashing a device

USB is the first flash of a device's life and, after that, only needed to
change its config. The runner in
`firmware/.cargo/config.toml` flashes the OTA layout via
`--partition-table partitions.csv`, so `cargo run --release --features net`
lays down the two-slot table automatically. See
[firmware/README.md](../firmware/README.md) for wiring and manual-flash
details.

### Release pipeline

Merging a firmware release PR tags the release, and the `firmware-image` job in
`.github/workflows/release.yml` attaches the image to it as
`firmware-vX.Y.Z.bin`. The job checks out the tag with full history so
`git describe` bakes in exactly that tag as the build id — the string the
device reports and the update check compares against.

It saves the **app image alone** (`espflash save-image`, no `--merge`): OTA
writes it straight into the spare app slot, leaving the bootloader and
partition table in place. `--partition-table partitions.csv` is passed so the
image is size-checked against the real 1.9 MB slot rather than espflash's
default layout.

### Backend firmware store

`FirmwareFetchWorker` polls the repo's releases every 30 minutes and caches
the newest published `firmware-v*` release that carries a `.bin` asset in the
`firmware_images` table (version, sha256, size, bytes). Drafts and prereleases
are skipped — a device must never install something not meant to ship — and
the app shares this release feed, hence the tag-prefix check. Every failure is
retried on the next tick rather than crashing the host; devices only ask
hourly, so a missed tick costs nothing.

Two endpoints serve the device:

| Route | Answer |
|-------|--------|
| `GET /api/firmware/latest?current=<build id>` | `204` when `current` already matches the cached image or nothing is cached; otherwise `{version, size, sha256}` |
| `GET /api/firmware/binary?version=<tag>` | the image bytes; `404` if that version is not cached |

The common hourly wake is the `204`: one short response and the device sleeps
again. The download passes back the version it was offered, so a release
landing between the check and the download cannot hand it bytes that fail the
sha256 it is verifying against.

That sha256 is an **integrity** check, not an authenticity one. The backend
computes it from the bytes it downloaded, which catches a corrupt download or
a bad flash write; it does not prove the release was not tampered with.
Authenticity rests on the backend's TLS connection to GitHub. Signed images
would be separate work.

### Device update flow

Each wake, after publishing its reading and before tearing down WiFi, the
device asks `GET /api/firmware/latest?current=<build id>`. The usual answer is
`204` and it goes straight to sleep.

When an update is offered it opens a second connection for
`GET /api/firmware/binary?version=<tag>` and streams the image into the app
slot it is **not** running, hashing as it goes — the image is ~440 KB against
~100 KB of heap, so nothing is buffered whole. Writes are collected into whole
4 KB sectors first: `esp-storage` erases a full sector per write, so passing
socket-sized chunks straight through would erase each sector eight times over.

Only once the image is complete and matches the advertised sha256 does the
device point the bootloader at that slot. Every failure — no answer, a
malformed offer, a truncated or corrupt image, a failed flash write — skips
the update and deep-sleeps as usual; the next wake retries from scratch. A
failed update costs one cycle and cannot brick the device, because the
running firmware is never touched.

The download carries its own 30 s budget and feeds the watchdog once per read.
A 441 KB transfer over weak WiFi can outlast what is left of the 60 s window
after the reading and the publish, and the watchdog cannot otherwise tell a
slow transfer from a hang.

### Rollback

A newly activated slot is marked `New`, not `Valid`, so the bootloader watches
its first boot. The image confirms itself once it has booted, read the sensor
and joined the network; an image that cannot get that far is rolled back to
the previous slot.

Confirmation deliberately does not depend on the broker or the backend
answering. An image that boots and networks is a good image, and reverting one
because Mosquitto happened to be down would be worse than the failure rollback
exists to catch.

### Device configuration

The backend is expected on the broker's host at `backend_port` — optional,
defaulting to 5001, so devices provisioned before OTA existed keep working
without being reprovisioned over USB.

## Still to prove

The whole path is host-tested, and the server half has been verified end to
end against a real release asset: CI attached `firmware-v0.4.0.bin`, the
backend cached it from GitHub, and `/api/firmware/binary` served bytes
identical to the asset with a matching sha256.

No device has actually installed an update yet. That wants a real
flash-and-watch: provision a device, flash a build one release behind, and
confirm it picks up the newer image on its next wake and reports the new
version in its reading.
