#!/bin/sh
# Cargo runner: flash and monitor over USB (see .cargo/config.toml).
#
# Clears `otadata` first. Once a device has taken an over-the-air update, that
# points at the slot the update landed in (ota_1), but espflash always writes
# the first app partition (ota_0) — so every later flash would land in a slot
# the device never boots. The flash succeeds, verifies, reboots, and the new
# code simply is not there, which looks exactly like a broken build.
#
# Erasing it makes the bootloader fall back to ota_0, so `cargo run` always
# runs what was just flashed. Only the boot pointer is cleared: the config
# partition at 0x9000 keeps the WiFi and backend settings, so this never costs
# a reprovision. It does discard OTA rollback state, which is what you want
# when deliberately flashing over USB.
set -e

espflash erase-region 0xd000 0x2000 --chip esp32c3
espflash flash --monitor --chip esp32c3 --partition-table partitions.csv "$@"
