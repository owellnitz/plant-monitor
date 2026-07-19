#!/bin/sh
# Write WiFi/MQTT settings into the device's `config` flash partition.
# Run once per device (and again only when the settings change); firmware
# updates leave this partition untouched.
#
#   ./provision.sh [config-file] [extra espflash args...]
#   ./provision.sh config.toml --port /dev/cu.usbserial-10
#
# The partition format is: magic "PMC1" + little-endian u32 length + the
# config text (parsed by src/config.rs). 0x9000 is the config partition
# offset from partitions.csv.
set -e

if [ $# -gt 0 ]; then
    CONFIG="$1"
    shift
else
    CONFIG="config.toml"
fi
[ -f "$CONFIG" ] || { echo "config file not found: $CONFIG" >&2; exit 1; }

OUT="$(mktemp)"
trap 'rm -f "$OUT"' EXIT

python3 - "$CONFIG" "$OUT" <<'PY'
import struct, sys
cfg = open(sys.argv[1], "rb").read()
if len(cfg) > 1024:                       # MAX_PAYLOAD in src/config.rs
    sys.exit("config too large (max 1024 bytes)")
with open(sys.argv[2], "wb") as f:
    f.write(b"PMC1" + struct.pack("<I", len(cfg)) + cfg)
PY

espflash write-bin 0x9000 "$OUT" --chip esp32c3 "$@"
echo "provisioned $CONFIG -> config partition (0x9000)"
