//! Sensor calibration tool: flash temporarily over USB, then hold the sensor
//! in air and dip it into water. Measures exactly like the sampler (same
//! burst, same trimmed mean) once a second and prints over serial:
//!
//!   raw 3129  mean 3130  spread 4  settled
//!   raw_dry = "4095"
//!   raw_wet = "3130"
//!
//! Copy the last `raw_dry`/`raw_wet` pair into the device's config file and
//! run provision.sh, then flash the sampler again. See README "Calibration".

#![no_std]
#![no_main]

use core::fmt::Write as _;

use esp_hal::{
    analog::adc::{Adc, AdcConfig, Attenuation},
    delay::Delay,
    main,
    time::{Duration, Instant},
    uart::{Config as UartConfig, UartTx},
};
use plant_monitor_firmware::sensor::{
    ADC_MAX, BURST_GAP_MS, BURST_SAMPLES, Calibrator, trimmed_mean,
};

/// Reboot on panic; the serial monitor shows the restart banner.
#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    esp_hal::system::software_reset()
}

esp_bootloader_esp_idf::esp_app_desc!();

#[main]
fn main() -> ! {
    let peripherals = esp_hal::init(esp_hal::Config::default());

    // UART0 on GPIO21 is the DevKitM-1's USB-serial bridge (espflash monitor).
    let mut serial = UartTx::new(peripherals.UART0, UartConfig::default())
        .unwrap()
        .with_tx(peripherals.GPIO21);

    // Same pin and attenuation as the sampler (bin/main.rs).
    let mut adc_config = AdcConfig::new();
    let mut pin = adc_config.enable_pin(peripherals.GPIO0, Attenuation::_11dB);
    let mut adc = Adc::new(peripherals.ADC1, adc_config);
    let delay = Delay::new();

    let _ = writeln!(
        serial,
        "calibrate: hold sensor in air ~30 s, then in water ~30 s"
    );
    let mut calibrator = Calibrator::new();
    let (mut driest, mut wettest) = (None, None);
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
        // Paste-ready lines whenever an endpoint moves. No trailing comment:
        // the config parser would read it as part of the value.
        if (calibrator.driest, calibrator.wettest) != (driest, wettest) {
            (driest, wettest) = (calibrator.driest, calibrator.wettest);
            if let Some(dry) = driest {
                let _ = writeln!(serial, "raw_dry = \"{dry}\"");
            }
            if let Some(wet) = wettest.filter(|&wet| Some(wet) != driest) {
                let _ = writeln!(serial, "raw_wet = \"{wet}\"");
            }
        }

        while start.elapsed() < Duration::from_secs(1) {}
    }
}
