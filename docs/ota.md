# Over-the-Air (OTA) Firmware Updates

Flash a device over USB once. Every later firmware release installs itself on
the device's hourly wake.

```
merge a firmware change → release PR → merge
   → CI builds a generic image, attaches it to the GitHub release
   → backend polls GitHub, caches the image in Postgres
   → device (hourly wake) asks the backend "newer than what I run?"
   → downloads to the spare flash slot, verifies, reboots into it
   → reports its new version in every reading
```

The device never talks to GitHub directly — there is no TLS on the no_std
ESP32-C3 — so the backend proxies and caches. A failed update never costs more
than one wake cycle: the running firmware is untouched until a fully downloaded
image verifies.

## How it works

### Flash layout

The device runs the ESP-IDF 2nd-stage bootloader with a two-slot OTA layout
([`firmware/partitions.csv`](../firmware/partitions.csv)) on the 4 MB flash:

| Partition | Purpose |
|-----------|---------|
| `config` (nvs, 0x9000) | WiFi/MQTT/backend settings, so a generic image runs on any device |
| `otadata` (0xd000) | records which app slot boots |
| `ota_0` (0x10000, ~1.9 MB) | app slot A |
| `ota_1` (0x1f0000, ~1.9 MB) | app slot B |

Two app slots are what OTA needs: an update is written to the *inactive* slot
and activated only once it verifies, so a bad image cannot brick the device.
The image is ~460 KB — roughly 4× headroom per slot.

### Firmware versioning

`build.rs` bakes a build id into every image via
`git describe --tags --match 'firmware-v*'` (exposed as `config::FW_BUILD`):

- on a firmware release commit → the exact tag, e.g. `firmware-v0.7.0`
- otherwise → `firmware-v0.7.0-<n>-g<hash>` (or `dev` outside a git checkout)

The device reports it as `fw` in every reading, and the update check compares
it against the cached release tag by string equality — no version parsing.

### Configuration

WiFi, MQTT and backend settings live in the `config` flash partition, not the
binary, so one image runs on any device. The partition holds a small framed
blob — magic `PMC1`, a little-endian length, then `key = "value"` text — read
and parsed at boot ([`firmware/src/config.rs`](../firmware/src/config.rs)).

| Key | Required | Notes |
|-----|----------|-------|
| `wifi_ssid`, `wifi_password` | yes | 2.4 GHz only |
| `mqtt_host` | yes | IPv4 only, no DNS. Also used as the backend's host |
| `mqtt_port` | yes | usually `1883` |
| `backend_port` | no | defaults to `5001` |

A missing or invalid partition means the device shows its reading and skips the
network — no build failure, no panic. A *malformed* value rejects the whole
config, so a typo takes the device off the network rather than silently
falling back.

Provision once per device with [`firmware/provision.sh`](../firmware/provision.sh);
the partition survives OTA updates.

### Release pipeline

Merging a firmware release PR tags the release, and the `firmware-image` job in
[`.github/workflows/release.yml`](../.github/workflows/release.yml) attaches the
image as `firmware-vX.Y.Z.bin`. It
checks out the tag with full history so `git describe` bakes in exactly that
tag — the string the device reports and the update check compares.

It saves the **app image alone** (`espflash save-image`, no `--merge`): OTA
writes it straight into the spare app slot, leaving the bootloader and
partition table in place. `--partition-table partitions.csv` is passed so the
image is size-checked against the real 1.9 MB slot.

### Backend firmware store

`FirmwareFetchWorker` polls the repo's releases every 30 minutes and caches the
newest published `firmware-v*` release carrying a `.bin` asset in the
`firmware_images` table (version, sha256, size, bytes). Drafts and prereleases
are skipped, and the app shares this release feed, hence the tag-prefix check.
Every failure is retried on the next tick rather than crashing the host.

| Route | Answer |
|-------|--------|
| `GET /api/firmware/latest?current=<build id>` | `204` when `current` already matches the cached image or nothing is cached; otherwise `{version, size, sha256}` |
| `GET /api/firmware/binary?version=<tag>` | the image bytes; `404` if that version is not cached |

The common hourly wake is the `204`. The download passes back the version it
was offered, so a release landing between the check and the download cannot
hand the device bytes that fail the sha256 it is verifying against.

Configuration lives in `appsettings.json`: `Firmware:GithubRepo` (empty
switches caching off), `Firmware:PollMinutes`, and `Firmware:GithubToken` for a
private repo.

### Device update flow

Each wake, after publishing its reading and before tearing down WiFi, the
device asks `GET /api/firmware/latest?current=<build id>`. The usual answer is
`204` and it goes straight to sleep.

When an update is offered it opens a second connection for
`GET /api/firmware/binary?version=<tag>` and streams the image into the app
slot it is **not** running, hashing as it goes — the image is ~460 KB against
~100 KB of heap, so nothing is buffered whole. Writes are collected into whole
4 KB sectors first: `esp-storage` erases a full sector per write, so passing
socket-sized chunks straight through would erase each sector eight times over.

Only once the image is complete and matches the advertised sha256 does the
device point the bootloader at that slot. Every failure — no answer, a
malformed offer, a truncated or corrupt image, a failed flash write — skips the
update and deep-sleeps as usual; the next wake retries from scratch.

The update check runs after the publish so a reading is never lost to a failed
update. The download carries its own 30 s **inactivity** budget — restarted
whenever bytes arrive — and feeds the watchdog once per read, because a 460 KB
transfer over weak WiFi is legitimately slower than the watchdog window.

The response to the update check carries no `Content-Length`: the backend
delimits it by closing the connection, which is correct HTTP/1.0 and what the
client asks for. The image download does send one.

### Rollback

A newly activated slot is marked `New`, not `Valid`, so the bootloader watches
its first boot. The image confirms itself once it has booted, read the sensor
and joined the network; an image that cannot get that far is rolled back to the
previous slot.

Confirmation deliberately does not depend on the broker or the backend
answering. An image that boots and networks is a good image, and reverting one
because Mosquitto happened to be down would be worse than the failure rollback
exists to catch.

### What the device reports

Every reading carries `fw` and `ota` alongside the measurement:

```json
{"id":"a1b2c3d4e5f6","raw":3500,"percent":62,"fw":"firmware-v0.7.0","reset":"deep_sleep","ota":"current"}
```

| `ota` | Meaning |
|-------|---------|
| `skipped` | no attempt — no network this cycle, or no usable config |
| `current` | the backend says this device already runs the newest image |
| `unreachable` | the backend could not be reached, or gave no usable answer |
| `installed` | an image was downloaded, verified and made the next boot's image |
| `failed` | an update was offered but downloading or verifying it failed |
| `unknown` | nothing recorded — the device was power-cycled |

The update runs *after* the publish, so a cycle cannot report its own outcome:
the result is kept in RTC memory across deep sleep and published on the next
wake, exactly as `reset` describes the previous boot. RTC memory is only
guaranteed zeroed on the first boot, so the stored value is tagged and anything
else reads as `unknown`.

This is the only way to see what an update attempt did without a serial cable.
The device has no console, and every OTA failure otherwise looks identical from
outside: readings keep arriving and the version never changes.

## Operating a device

### First flash

USB, once per device:

```sh
cd firmware
cp config.example.toml config.toml     # fill in wifi/mqtt/backend settings
./provision.sh                          # writes config to 0x9000
cargo run --release --features net      # flashes the OTA layout, then monitors
```

Every later release arrives over WiFi. Toolchain, wiring and manual-flash
details: [firmware/README.md](../firmware/README.md).

### Reflashing a device that has already updated itself

Once an update lands in `ota_1`, `otadata` points there, but `espflash` always
writes the **first** app partition, `ota_0`. A plain `espflash flash` then lands
in a slot the device never boots: it succeeds, verifies, reboots, and the new
code is simply not there.

`cargo run` handles this — its runner ([`firmware/flash.sh`](../firmware/flash.sh))
erases `otadata` first, so the bootloader falls back to `ota_0`. Flashing by hand needs the same
step:

```sh
espflash erase-region 0xd000 0x2000 --chip esp32c3
```

That clears only the boot pointer. The `config` partition is untouched, so no
reprovisioning.

### Reading the boot log

Two lines confirm the device is set up correctly. The partition table must list
the OTA layout:

```
I (62) boot:  0 config    WiFi data  01 02 00009000 00004000
I (68) boot:  1 otadata   OTA data   01 00 0000d000 00002000
I (75) boot:  2 ota_0     OTA app    00 10 00010000 001e0000
I (82) boot:  3 ota_1     OTA app    00 11 001f0000 001e0000
```

and the slot the bootloader runs should be the one just written:

```
I (211) boot: Loaded app from partition at offset 0x10000
```

## Troubleshooting

| Symptom | Cause |
|---------|-------|
| Readings arrive, version never changes | Check the `ota` field. `unreachable` means the device cannot reach the backend — most often `backend_port` not matching where the backend is published. `current` means the backend has not cached a newer release yet. |
| Boot log shows `factory` rather than `ota_0`/`ota_1` | Flashed without `--partition-table partitions.csv`. OTA cannot work at all; reflash with `cargo run`. |
| Flash succeeds but the new code is not running | `otadata` points at the other slot — see reflashing above. The boot log's `Loaded app from partition at offset` will not match where espflash wrote. |
| Device drops off the network after provisioning | A malformed config value rejects the whole config. Check `config.toml` and reprovision. |
| Backend never caches a new release | The release needs a published (non-draft, non-prerelease) `firmware-v*` tag with a `.bin` asset. `docker logs <backend> \| grep -i "cached firmware"`. Restarting the backend forces a poll. |

## Limitations

**Integrity, not authenticity.** The sha256 is computed by the backend from the
bytes it downloaded. It catches a corrupt download or a bad flash write, not a
tampered release; authenticity rests on the backend's TLS connection to GitHub.
Signed images would be separate work.

**Newer versus different.** The backend offers an update whenever the device's
version string differs from the cached one — it has no notion of newer versus
older, by the deliberate choice of string equality with no version parsing.
Release-to-release updates are always forward, but three cases bite:

- A **dev build** (`firmware-v0.7.0-3-gabc1234`) never equals a release tag, so
  it is always offered the latest release even when its code is newer.
- A **release whose asset job failed** is skipped by the poller, so a device
  USB-flashed with that version is pulled back to the previous release.
- A **broken release** that installs, boots badly and is rolled back leaves the
  device differing from latest again, so it retries every wake until the
  release is pulled or fixed.

**Devices on `firmware-v0.5.0` or earlier cannot update themselves.** Their
update check cannot read a response without a `Content-Length`, which is what
the backend sends. Each needs one USB flash of a later build; OTA is
self-sustaining from there.
