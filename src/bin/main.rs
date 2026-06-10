//! Shows the soil moisture reading on a Waveshare 0.96" SPI OLED (SSD1315,
//! SSD1306-compatible) and lights the onboard WS2812 RGB LED (GPIO8) of the
//! ESP32-C3-DevKitM-1.
//!
//! OLED wiring: DIN=GPIO6, CLK=GPIO4, CS=GPIO7, D/C=GPIO5, RES=GPIO10
//! Grove capacitive moisture sensor: yellow (signal) = GPIO0, red = 3V3, black = GND

#![no_std]
#![no_main]

use core::fmt::Write as _;

use embedded_graphics::{
    mono_font::{MonoTextStyle, ascii::FONT_10X20},
    pixelcolor::BinaryColor,
    prelude::*,
    text::{Alignment, Text},
};
use embedded_hal_bus::spi::ExclusiveDevice;
use esp_hal::{
    analog::adc::{Adc, AdcConfig, Attenuation},
    delay::Delay,
    gpio::{Level, Output, OutputConfig},
    main,
    rmt::Rmt,
    spi::{
        Mode,
        master::{Config as SpiConfig, Spi},
    },
    time::Rate,
};
use esp_hal_smartled::{SmartLedsAdapter, smart_led_buffer};
use smart_leds::{RGB8, SmartLedsWrite, brightness, gamma};
use ssd1306::{Ssd1306, prelude::*};

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}

// This creates a default app-descriptor required by the esp-idf bootloader.
esp_bootloader_esp_idf::esp_app_desc!();

#[main]
fn main() -> ! {
    let peripherals = esp_hal::init(esp_hal::Config::default());
    let mut delay = Delay::new();

    // Onboard WS2812 LED via RMT.
    let rmt = Rmt::new(peripherals.RMT, Rate::from_mhz(80)).expect("Failed to initialize RMT");
    let mut rmt_buffer = smart_led_buffer!(1);
    let mut led = SmartLedsAdapter::new(rmt.channel0, peripherals.GPIO8, &mut rmt_buffer);

    // OLED on SPI2.
    let spi = Spi::new(
        peripherals.SPI2,
        SpiConfig::default()
            .with_frequency(Rate::from_mhz(8))
            .with_mode(Mode::_0),
    )
    .expect("Failed to initialize SPI")
    .with_sck(peripherals.GPIO4)
    .with_mosi(peripherals.GPIO6);

    let cs = Output::new(peripherals.GPIO7, Level::High, OutputConfig::default());
    let dc = Output::new(peripherals.GPIO5, Level::Low, OutputConfig::default());
    let mut rst = Output::new(peripherals.GPIO10, Level::High, OutputConfig::default());

    let spi_device = ExclusiveDevice::new(spi, cs, delay).expect("Failed to create SPI device");
    let interface = SPIInterface::new(spi_device, dc);
    let mut display = Ssd1306::new(interface, DisplaySize128x64, DisplayRotation::Rotate0)
        .into_buffered_graphics_mode();
    display.reset(&mut rst, &mut delay).expect("Display reset failed");
    display.init().expect("Display init failed");

    // Moisture sensor on GPIO0 / ADC1. 11 dB attenuation covers the sensor's
    // full 0..~3.1 V output range.
    let mut adc_config = AdcConfig::new();
    let mut moisture_pin = adc_config.enable_pin(peripherals.GPIO0, Attenuation::_11dB);
    let mut adc = Adc::new(peripherals.ADC1, adc_config);

    let style = MonoTextStyle::new(&FONT_10X20, BinaryColor::On);
    let delay = Delay::new();
    let blue = RGB8::new(0, 0, 255);
    let level = 30;

    led.write(brightness(gamma([blue].into_iter()), level))
        .unwrap();

    // Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
    const RAW_DRY: u16 = 4095;
    const RAW_WET: u16 = 3130;

    loop {
        let raw: u16 = nb::block!(adc.read_oneshot(&mut moisture_pin)).unwrap();

        let clamped = raw.clamp(RAW_WET, RAW_DRY);
        let percent = (RAW_DRY - clamped) as u32 * 100 / (RAW_DRY - RAW_WET) as u32;

        let mut text: heapless::String<32> = heapless::String::new();
        write!(text, "Moisture\n{percent}%").unwrap();

        display.clear(BinaryColor::Off).unwrap();
        // Two lines of FONT_10X20 (20 px line height) on a 64 px screen:
        // block top = (64 - 40) / 2 = 12, first baseline = top + 16 = 28.
        Text::with_alignment(&text, Point::new(64, 28), style, Alignment::Center)
            .draw(&mut display)
            .unwrap();
        display.flush().unwrap();

        delay.delay_millis(1000);
    }
}
