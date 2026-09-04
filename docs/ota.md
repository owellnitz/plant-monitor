# Over-the-Air (OTA) Firmware Updates

Reference for the OTA feature. The goal — flash a device over USB exactly
once, then deliver every later firmware update over WiFi — is implemented and
has been proven on hardware.

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

## Proven on hardware

A device has taken an update over the air end to end, twice — once before the
fixes below were found, and once with the shipped code afterwards. On the
second run it asked the backend, was offered `firmware-v0.6.0`, streamed
460,128 bytes into the slot it was not running, verified the sha256, swapped
the boot slot, and came back up from `ota_1` (`0x1f0000`) running the new
image, spinner and all.

The server half was verified against the same release: CI attached
`firmware-v0.5.0.bin`, the backend cached it from GitHub, and
`/api/firmware/binary` served bytes identical to the asset.

### What the first hardware run cost

Everything up to this point was host-tested and still failed on the device,
in three ways worth recording:

1. **The device was flashed without the partition table.** espflash's default
   single-app layout has no `otadata` and no second slot, so OTA cannot work
   at all. Use `cargo run`, whose runner passes `--partition-table
   partitions.csv`, and check the boot log lists `config`/`otadata`/`ota_0`/
   `ota_1`.
2. **The update check has no `Content-Length`.** The backend delimits that
   response by closing the connection — correct HTTP/1.0 — but the client
   required a length, so it discarded every offer silently. The image download
   is unaffected; that endpoint does send one.
3. **The download timeout capped duration rather than inactivity.** 457 KB
   cannot finish inside 30 s here: smoltcp's small receive buffer means about
   one segment per round trip, and every 4 KB sector costs a flash
   erase-write. The deadline now restarts whenever bytes arrive.

The common thread is that the firmware has **no diagnostics**. Each failure
looked identical from outside — readings kept arriving and the version never
changed. Temporary serial logging found both code bugs within minutes after
three wrong guesses without it. The device reports `reset` in every payload
for exactly this reason; an equivalent `ota` status field would make these
failures visible in the backend log without a serial cable.

### USB flashing stops working once a device has taken an update

The most confusing failure of all, and not a bug — the OTA code doing exactly
its job. After an update, `otadata` points at the slot the update landed in
(`ota_1`). `espflash` does not consult `otadata`: it writes the **first** app
partition, `ota_0`. So every later `cargo run` writes a slot the device never
boots, and the device keeps running the OTA-installed image.

Nothing reports this. The flash succeeds and verifies, the device reboots, and
the new code simply is not there — no new behaviour, no version change. It
looks exactly like a broken build.

The boot log is the tell. These two lines have to name the same address:

```
[00:00:32] [====] 19/19   0x10000   Verifying... OK!        <- espflash wrote ota_0
I (211) boot: Loaded app from partition at offset 0x1f0000   <- bootloader ran ota_1
```

Clear the boot pointer so the bootloader falls back to `ota_0`, then flash:

```sh
espflash erase-region 0xd000 0x2000 --port /dev/cu.usbserial-210
cargo run --release --features net
```

That erases `otadata` only. The `config` partition at `0x9000` is untouched, so
WiFi and `backend_port` survive and no reprovisioning is needed.

### Upgrading a device that predates the fixes

`firmware-v0.5.0` and earlier cannot update themselves, because their update
check is the one that could not read a close-delimited response. Each device
needs one USB flash of a build containing the fix; OTA is self-sustaining
from there.

### Known limitation: newer versus different

The backend offers an update whenever the device's version string differs
from the cached one — it has no notion of newer versus older, by the
deliberate choice of string equality with no version parsing. Three cases
where that bites:

- A **dev build** (`firmware-v0.5.0-3-gabc1234`) never equals a release tag,
  so it is always offered the latest release even when its code is newer.
- A **release whose asset job failed** is skipped by the poller, so a device
  USB-flashed with that version is pulled back to the previous release.
- A **broken release** that installs, boots badly and is rolled back leaves
  the device differing from latest again, so it retries the same update every
  wake until the release is pulled or fixed.

None affect the steady state of release-to-release updates, which are always
forward.
