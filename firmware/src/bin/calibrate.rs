//! Sensor calibration tool: flash temporarily over USB and follow the prompts
//! in the serial monitor. Measures exactly like the sampler (same burst, same
//! trimmed mean) once a second and walks through two steps, each confirmed
//! with Enter once the reading has settled:
//!
//!   1. sensor in air    -> raw_dry
//!   2. sensor in water  -> raw_wet
//!
//! Then it prints the paste-ready pair for the device's config file:
//!
//!   raw_dry = "4095"
//!   raw_wet = "3130"
//!
//! Run provision.sh with that file, then flash the sampler again. See README
//! "Calibration".

#![no_std]
#![no_main]

use core::fmt::Write as _;

use esp_hal::{
    analog::adc::{Adc, AdcConfig, Attenuation},
    delay::Delay,
    main,
    time::{Duration, Instant},
    uart::{Config as UartConfig, Uart},
};
use plant_monitor_firmware::sensor::{
    ADC_MAX, BURST_GAP_MS, BURST_SAMPLES, Calibration, Calibrator, MIN_SPAN, trimmed_mean,
};

const STEP_1: &str = "step 1/2: hold the sensor in air, press Enter once it reads `settled`";

/// Reboot on panic; the serial monitor shows the restart banner.
#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    esp_hal::system::software_reset()
}

esp_bootloader_esp_idf::esp_app_desc!();

#[main]
fn main() -> ! {
    let peripherals = esp_hal::init(esp_hal::Config::default());

    // UART0 (TX GPIO21, RX GPIO20) is the DevKitM-1's USB-serial bridge:
    // espflash monitor shows what we print and forwards keypresses.
    let mut serial = Uart::new(peripherals.UART0, UartConfig::default())
        .unwrap()
        .with_tx(peripherals.GPIO21)
        .with_rx(peripherals.GPIO20);

    // Same pin and attenuation as the sampler (bin/main.rs).
    let mut adc_config = AdcConfig::new();
    let mut pin = adc_config.enable_pin(peripherals.GPIO0, Attenuation::_11dB);
    let mut adc = Adc::new(peripherals.ADC1, adc_config);
    let delay = Delay::new();

    let _ = writeln!(serial, "{STEP_1}");
    let mut calibrator = Calibrator::default();
    let mut dry: Option<u16> = None;
    loop {
        let start = Instant::now();
        let mut samples = [0u16; BURST_SAMPLES];
        for sample in samples.iter_mut() {
            *sample = nb::block!(adc.read_oneshot(&mut pin)).unwrap();
            delay.delay_millis(BURST_GAP_MS);
        }
        let raw = trimmed_mean(&mut samples);
        let stats = calibrator.push(raw);

        let _ = writeln!(
            serial,
            "raw {raw}  mean {}  spread {}  {}{}",
            stats.mean,
            stats.spread,
            if stats.settled { "settled" } else { "settling" },
            if raw >= ADC_MAX { "  CLIPPED" } else { "" },
        );

        // Drain everything typed since the last line, so a queued key press
        // can't carry over and confirm the next step: Enter confirms, `r`
        // starts over at step 1.
        let (mut enter, mut restart) = (false, false);
        let mut typed = [0u8; 16];
        while let Ok(n) = serial.read_buffered(&mut typed) {
            if n == 0 {
                break;
            }
            for &b in &typed[..n] {
                match b {
                    b'\r' | b'\n' => enter = true,
                    b'r' => restart = true,
                    _ => {}
                }
            }
        }

        match (restart, enter, stats.settled, dry) {
            (true, ..) => {
                dry = None;
                calibrator.reset();
                let _ = writeln!(serial, "{STEP_1}");
            }
            (false, false, ..) => {}
            (false, true, false, _) => {
                let _ = writeln!(serial, "not settled yet, wait for `settled`");
            }
            (false, true, true, None) => {
                dry = Some(stats.mean);
                calibrator.reset();
                let _ = writeln!(
                    serial,
                    "dry = {}{}\nstep 2/2: dip the sensor into water up to the line, \
                     press Enter once it reads `settled` (r = redo step 1)",
                    stats.mean,
                    if stats.mean >= ADC_MAX {
                        " (clipped at ADC max)"
                    } else {
                        ""
                    },
                );
            }
            (false, true, true, Some(dry)) => match Calibration::new(dry, stats.mean) {
                Some(cal) => {
                    // No trailing comment: the config parser would read it
                    // as part of the value.
                    let _ = writeln!(
                        serial,
                        "done, copy into the device's config file:\n\
                         raw_dry = \"{}\"\nraw_wet = \"{}\"",
                        cal.raw_dry, cal.raw_wet
                    );
                    loop {
                        delay.delay_millis(1000);
                    }
                }
                None => {
                    // Must settle anew, so the same window can't be
                    // confirmed again unchanged.
                    calibrator.reset();
                    let _ = writeln!(
                        serial,
                        "wet {} is not {MIN_SPAN}+ below dry {dry}: is the sensor in water? \
                         (r = redo step 1)",
                        stats.mean
                    );
                }
            },
        }

        while start.elapsed() < Duration::from_secs(1) {}
    }
}
