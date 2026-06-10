# esp32-poc

Bare-metal Rust (`no_std`) firmware for the **ESP32-C3-DevKitM-1**:

- Lights the onboard WS2812 RGB LED (GPIO8) solid blue.
- Reads a Grove capacitive soil moisture sensor via ADC and shows the value as a
  percentage on a Waveshare 0.96" SPI OLED (SSD1315 controller, SSD1306-compatible,
  128x64, monochrome), refreshed every second.
- Connects to WiFi (DHCP) and publishes the reading once per second as JSON to an
  MQTT broker: topic `sensors/<device_id>/moisture`, payload
  `{"id":"plant-1","raw":3500,"percent":62}` (QoS 0).

## Hardware

| Part | Detail |
|------|--------|
| Board | ESP32-C3-DevKitM-1 (ESP32-C3-MINI-1 module, RISC-V, 4 MB flash) |
| Display | Waveshare 0.96" OLED, SPI, pins: VCC GND NC DIN CLK CS D/C RES |
| Sensor | Grove capacitive soil moisture sensor (analog, 4-wire Grove cable) |
| Serial port (macOS) | `/dev/cu.usbserial-210` (onboard USB-UART bridge) |
| Misc | Breadboard, 7 jumper wires, USB cable |

## Local setup (macOS, from scratch)

```sh
# 1. Rust toolchain (Homebrew's rustup is keg-only, hence the PATH line)
brew install rustup
export PATH="$HOME/.cargo/bin:/opt/homebrew/opt/rustup/bin:$PATH"  # also added to ~/.zshrc
rustup default stable

# 2. Cross-compilation target for the ESP32-C3 (RISC-V)
rustup target add riscv32imc-unknown-none-elf

# 3. Flashing tool
brew install espflash
```

No espup / Xtensa fork / nightly needed — the C3 is RISC-V and works on stable Rust.

The project skeleton was generated with [esp-generate](https://github.com/esp-rs/esp-generate)
(`esp-generate --chip esp32c3 --headless`), then the display/LED dependencies were added.

### Version pinning (important)

`esp-hal-smartled 0.17` requires `esp-hal ~1.0` (not 1.1) and pairs with
`esp-bootloader-esp-idf 0.4`. Don't bump `esp-hal` without checking
`esp-hal-smartled` compatibility first.

The WiFi stack is pinned to the same `esp-hal 1.0` line: `esp-radio 0.17` +
`esp-rtos 0.2` + `esp-alloc 0.9` + `smoltcp 0.12` (`esp-radio 0.18` requires
`esp-hal 1.1`). `blocking-network-stack` is git-only (not on crates.io), pinned
to a rev.

## Wiring

OLED is driven via SPI2. All connections are direct row-to-row jumpers on the breadboard
(no power rails used).

| OLED pin | ESP32 pin (silkscreen label) | Function |
|----------|------------------------------|----------|
| VCC | 3V3 | Power (3.3 V — never 5 V) |
| GND | GND | Ground |
| NC | — | Not connected |
| DIN | 6 (GPIO6) | SPI MOSI |
| CLK | 4 (GPIO4) | SPI clock |
| CS | 7 (GPIO7) | Chip select |
| D/C | 5 (GPIO5) | Data/command select |
| RES | 10 (GPIO10) | Display reset |

Breadboard layout: devkit straddles the center groove at rows 1–15 (pin columns b and i),
USB connector facing the board edge. OLED header sits at rows 20–27, column a; jumpers
attach in columns b–e of the same rows.

Moisture sensor (Grove cable colors):

| Grove wire | ESP32 pin | Function |
|------------|-----------|----------|
| Red | 3V3 | Power |
| Black | GND | Ground |
| Yellow | 0 (GPIO0) | Analog signal → ADC1 |
| White | — | Not connected |

Calibration (2026-06-10): raw ADC 4095 = dry in air (clipped at ADC max), 3130 = in
water; mapped linearly to 0–100 %. Re-measure `RAW_DRY`/`RAW_WET` in `src/sensor.rs`
if the sensor or supply changes.

Onboard WS2812 LED is fixed on GPIO8 (driven via the RMT peripheral) — avoid GPIO8 for
external wiring, and stay away from strapping pins GPIO2 and GPIO9 entirely.

## Configuration

WiFi and MQTT settings are baked into the firmware at build time from `config.toml`
(gitignored — it holds the WiFi password):

```sh
cp config.example.toml config.toml
# then edit: wifi_ssid, wifi_password, mqtt_host (IPv4 only, no DNS),
# mqtt_port, device_id
```

`build.rs` turns each entry into a `CFG_*` env var consumed by `src/config.rs`.
Changing `config.toml` triggers a rebuild; a missing file fails the build with a
hint.

For a local test broker:

```sh
brew install mosquitto
mosquitto -v                                # broker, foreground
mosquitto_sub -t 'sensors/#' -v             # watch readings arrive
```

Note: the MQTT client does QoS 0 without authentication — fine for a home LAN,
nothing more.

## Build & flash

```sh
cargo build --release          # build only
cargo run --release            # build + flash + serial monitor (Ctrl+C exits monitor)
```

`cargo run` uses the runner configured in `.cargo/config.toml`
(`espflash flash --monitor --chip esp32c3`).

Flash manually without monitor:

```sh
espflash flash target/riscv32imc-unknown-none-elf/release/esp32-poc \
  --port /dev/cu.usbserial-210 --chip esp32c3
```

## Tests

Pure logic (MQTT packet encoding, moisture calibration) has host-run unit tests —
no hardware needed:

```sh
cargo test --lib --target aarch64-apple-darwin
```

This works because hardware dependencies are scoped to the riscv32 target in
`Cargo.toml`, the lib is `no_std` only outside of tests, and `build.rs` skips the
ESP linker scripts for non-riscv targets.

## Troubleshooting

**`Error while connecting to device`**
1. Another process may hold the port — close any running serial monitor
   (`lsof /dev/cu.usbserial-210` shows the holder).
2. The USB-UART bridge wedges occasionally: unplug USB, wait 5 s, replug.
3. Force bootloader by hand: hold BOOT, tap RST, release BOOT, then flash with
   `--before no-reset`.

**Serial dead but LED blinks / behaves oddly after breadboard work**
Bad devkit seating can short or disconnect the UART pins while the chip itself still
boots. Pull the devkit out of the breadboard and reinsert it evenly. Verify with
`espflash board-info` before suspecting the wiring.

**Stuck on "WiFi retry" / "DHCP..." / "MQTT..."**
The OLED shows the boot phase. "WiFi retry" = association failed (SSID/password
wrong, AP out of range — C3 is 2.4 GHz only). Stuck at "DHCP..." = joined but no
lease. Stuck at "MQTT..." = TCP to the broker failed; check `mqtt_host` is the
broker's IPv4, broker is running, and port 1883 is reachable
(`nc -vz <broker-ip> 1883` from the same network).

**Display stays black**
Normal until firmware that drives it is flashed (OLED pixels emit light only when
driven — there is no backlight). Otherwise re-check DIN/CLK/CS/D-C/RES against the
wiring table and confirm VCC is on 3V3.
