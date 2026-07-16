//! Hourly soil-moisture sampler on the ESP32-C3-DevKitM-1: wakes from deep
//! sleep, reads the sensor (trimmed mean of an ADC burst), shows the value on a
//! Waveshare 0.96" SPI OLED (SSD1315, SSD1306-compatible), publishes it via
//! MQTT (`net` feature only), then deep-sleeps for an hour. The OLED keeps
//! showing the last value from its own RAM while the chip sleeps; the WS2812
//! LED (GPIO8) is lit blue only while awake.
//!
//! OLED wiring: DIN=GPIO6, CLK=GPIO4, CS=GPIO7, D/C=GPIO5, RES=GPIO10
//! Grove capacitive moisture sensor: yellow (signal) = GPIO0, red = 3V3, black = GND
//!
//! WiFi/MQTT settings come from `config.toml` (see config.example.toml),
//! baked in at build time. Publishes JSON to `sensors/<device_id>/moisture`.

#![no_std]
#![no_main]

extern crate alloc;

use core::fmt::Write as _;
#[cfg(feature = "net")]
use core::net::Ipv4Addr;
use core::time::Duration;

#[cfg(feature = "net")]
use blocking_network_stack::Stack;
use embedded_graphics::{
    mono_font::{MonoTextStyle, ascii::FONT_10X20},
    pixelcolor::BinaryColor,
    prelude::*,
    text::{Alignment, Text},
};
use embedded_hal_bus::spi::ExclusiveDevice;
use esp_hal::{
    analog::adc::{Adc, AdcConfig, Attenuation},
    clock::CpuClock,
    delay::Delay,
    gpio::{Level, Output, OutputConfig},
    main, ram,
    rmt::Rmt,
    rtc_cntl::{Rtc, sleep::TimerWakeupSource},
    spi::{
        Mode,
        master::{Config as SpiConfig, Spi},
    },
    time::Rate,
};
#[cfg(feature = "net")]
use esp_hal::{interrupt::software::SoftwareInterruptControl, rng::Rng, timer::timg::TimerGroup};
use esp_hal_smartled::{SmartLedsAdapter, smart_led_buffer};
#[cfg(feature = "net")]
use esp_radio::wifi::{ClientConfig, ModeConfig, PowerSaveMode};
use plant_monitor_firmware::sensor::{moisture_percent, trimmed_mean};
#[cfg(feature = "net")]
use plant_monitor_firmware::{
    config::{DEVICE_ID, MQTT_HOST, MQTT_PORT, WIFI_PASSWORD, WIFI_SSID},
    mqtt,
};
use smart_leds::{RGB8, SmartLedsWrite, brightness, gamma};
#[cfg(feature = "net")]
use smoltcp::{
    iface::{SocketSet, SocketStorage},
    wire::{DhcpOption, IpAddress},
};
use ssd1306::{Ssd1306, prelude::*};

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}

// This creates a default app-descriptor required by the esp-idf bootloader.
esp_bootloader_esp_idf::esp_app_desc!();

/// OLED control lines: CLK=GPIO4, D/C=GPIO5, DIN=GPIO6, CS=GPIO7, RES=GPIO10.
const DISPLAY_PIN_MASK: u32 = 1 << 4 | 1 << 5 | 1 << 6 | 1 << 7 | 1 << 10;

/// Freezes/releases the OLED pads across deep sleep. Digital pads float in
/// deep sleep and a drifting RES line resets the SSD1315, blanking the
/// display. esp-hal exposes pad hold only for GPIO0-5 on the C3, so this
/// drives RTC_CNTL directly (DIG_PAD_HOLD bit index = GPIO number); the
/// sequence mirrors ESP-IDF's gpio_hold_en + gpio_deep_sleep_hold_en.
fn hold_display_pins(hold: bool) {
    let rtc_cntl = unsafe { esp32c3::RTC_CNTL::steal() };
    if hold {
        rtc_cntl
            .dig_pad_hold()
            .modify(|r, w| unsafe { w.bits(r.bits() | DISPLAY_PIN_MASK) });
        rtc_cntl.dig_iso().modify(|_, w| {
            w.dg_pad_force_unhold()
                .clear_bit()
                .dg_pad_autohold_en()
                .set_bit()
        });
    } else {
        rtc_cntl
            .dig_pad_hold()
            .modify(|r, w| unsafe { w.bits(r.bits() & !DISPLAY_PIN_MASK) });
        // Drop the autohold latched at sleep entry: pulse the global
        // force-unhold, then clear it again so later holds take effect.
        rtc_cntl.dig_iso().modify(|_, w| {
            w.dg_pad_autohold_en()
                .clear_bit()
                .dg_pad_force_unhold()
                .set_bit()
        });
        rtc_cntl
            .dig_iso()
            .modify(|_, w| w.dg_pad_force_unhold().clear_bit());
    }
}

#[main]
fn main() -> ! {
    // WiFi needs max CPU clock and a heap (radio blobs allocate).
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 36 * 1024);
    let mut delay = Delay::new();

    // Pads may still be frozen from the last deep sleep — release them
    // before anything tries to drive the display.
    hold_display_pins(false);

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
    display
        .reset(&mut rst, &mut delay)
        .expect("Display reset failed");
    display.init().expect("Display init failed");

    let style = MonoTextStyle::new(&FONT_10X20, BinaryColor::On);

    // Two lines of FONT_10X20 (20 px line height) on a 64 px screen:
    // block top = (64 - 40) / 2 = 12, first baseline = top + 16 = 28.
    macro_rules! show {
        ($($arg:tt)*) => {{
            let mut text: heapless::String<64> = heapless::String::new();
            let _ = write!(text, $($arg)*);
            display.clear(BinaryColor::Off).unwrap();
            Text::with_alignment(&text, Point::new(64, 28), style, Alignment::Center)
                .draw(&mut display)
                .unwrap();
            display.flush().unwrap();
        }};
    }

    // Moisture sensor on GPIO0 / ADC1. 11 dB attenuation covers the sensor's
    // full 0..~3.1 V output range.
    let mut adc_config = AdcConfig::new();
    let mut moisture_pin = adc_config.enable_pin(peripherals.GPIO0, Attenuation::_11dB);
    let mut adc = Adc::new(peripherals.ADC1, adc_config);

    let delay = Delay::new();
    let blue = RGB8::new(0, 0, 255);
    let level = 30;

    led.write(brightness(gamma([blue].into_iter()), level))
        .unwrap();

    // One measurement per wakeup, taken before the radio comes up (WiFi
    // activity adds ADC noise). Boot leaves the supply rail and ADC drifting
    // for a moment, and a back-to-back burst lands entirely on that drift —
    // so let things settle, then spread the samples over ~300 ms and average
    // the middle half (spike-robust, and the drift cancels out).
    delay.delay_millis(150);
    let mut samples = [0u16; 64];
    for sample in samples.iter_mut() {
        *sample = nb::block!(adc.read_oneshot(&mut moisture_pin)).unwrap();
        delay.delay_millis(5);
    }
    let raw = trimmed_mean(&mut samples);
    let percent = moisture_percent(raw);

    show!("Moisture\n{percent}%");

    // WiFi: esp-radio needs the esp-rtos scheduler running.
    #[cfg(feature = "net")]
    let timg0 = TimerGroup::new(peripherals.TIMG0);
    #[cfg(feature = "net")]
    let sw_int = SoftwareInterruptControl::new(peripherals.SW_INTERRUPT);
    #[cfg(feature = "net")]
    esp_rtos::start(timg0.timer0, sw_int.software_interrupt0);

    #[cfg(feature = "net")]
    let radio = esp_radio::init().expect("Failed to initialize radio");
    #[cfg(feature = "net")]
    let (mut controller, interfaces) =
        esp_radio::wifi::new(&radio, peripherals.WIFI, Default::default())
            .expect("Failed to initialize WiFi");

    #[cfg(feature = "net")]
    let mut device = interfaces.sta;
    #[cfg(feature = "net")]
    let iface = smoltcp::iface::Interface::new(
        smoltcp::iface::Config::new(smoltcp::wire::HardwareAddress::Ethernet(
            smoltcp::wire::EthernetAddress::from_bytes(&device.mac_address()),
        )),
        &mut device,
        smoltcp_now(),
    );

    #[cfg(feature = "net")]
    let mut socket_set_entries: [SocketStorage; 3] = Default::default();
    #[cfg(feature = "net")]
    let mut socket_set = SocketSet::new(&mut socket_set_entries[..]);
    #[cfg(feature = "net")]
    let mut dhcp_socket = smoltcp::socket::dhcpv4::Socket::new();
    #[cfg(feature = "net")]
    let hostname_option = [DhcpOption {
        kind: 12, // hostname
        data: DEVICE_ID.as_bytes(),
    }];
    #[cfg(feature = "net")]
    dhcp_socket.set_outgoing_options(&hostname_option);
    #[cfg(feature = "net")]
    socket_set.add(dhcp_socket);

    #[cfg(feature = "net")]
    let rng = Rng::new();
    #[cfg(feature = "net")]
    let now = || {
        esp_hal::time::Instant::now()
            .duration_since_epoch()
            .as_millis()
    };
    #[cfg(feature = "net")]
    let stack = Stack::new(iface, device, socket_set, now, rng.random());

    #[cfg(feature = "net")]
    {
        controller.set_power_saving(PowerSaveMode::None).unwrap();
        controller
            .set_config(&ModeConfig::Client(
                ClientConfig::default()
                    .with_ssid(WIFI_SSID.into())
                    .with_password(WIFI_PASSWORD.into()),
            ))
            .unwrap();
        controller.start().unwrap();

        controller.connect().unwrap();
        loop {
            match controller.is_connected() {
                Ok(true) => break,
                Ok(false) => {}
                Err(_) => {
                    // Wrong password, AP not found, etc. — wait and retry.
                    delay.delay_millis(5000);
                    let _ = controller.connect();
                }
            }
        }

        loop {
            stack.work();
            if stack.is_iface_up() {
                break;
            }
        }
    }

    #[cfg(feature = "net")]
    let broker: Ipv4Addr = MQTT_HOST
        .parse()
        .expect("config: mqtt_host is not an IPv4 address");
    #[cfg(feature = "net")]
    let port: u16 = MQTT_PORT
        .parse()
        .expect("config: mqtt_port is not a number");

    #[cfg(feature = "net")]
    let mut topic: heapless::String<64> = heapless::String::new();
    #[cfg(feature = "net")]
    write!(topic, "sensors/{DEVICE_ID}/moisture").unwrap();

    #[cfg(feature = "net")]
    let mut rx_buffer = [0u8; 1536];
    #[cfg(feature = "net")]
    let mut tx_buffer = [0u8; 1536];
    #[cfg(feature = "net")]
    let mut socket = stack.get_socket(&mut rx_buffer, &mut tx_buffer);

    #[cfg(feature = "net")]
    {
        // Reset-reason diagnostic: readings should only ever follow a deep
        // sleep wake (CoreDeepSleep) or a flash/power-cycle. Anything else
        // (SysBrownOut, watchdogs, ...) means the device rebooted instead of
        // sleeping and explains duplicate readings. The backend ignores
        // unknown JSON fields; watch via `mosquitto_sub -t 'sensors/#' -v`.
        let mut payload: heapless::String<160> = heapless::String::new();
        match esp_hal::system::reset_reason() {
            Some(r) => write!(
                payload,
                r#"{{"id":"{DEVICE_ID}","raw":{raw},"percent":{percent},"reset":"{r:?}"}}"#
            ),
            None => write!(
                payload,
                r#"{{"id":"{DEVICE_ID}","raw":{raw},"percent":{percent},"reset":"Unknown"}}"#
            ),
        }
        .unwrap();

        // Runs silently in the background — the moisture value stays on screen.
        // Broker may be unreachable: open_with_timeout bails after 5 s instead
        // of spinning forever, so publish_cycle just skips this cycle. Next wake
        // retries fresh.
        mqtt::publish_cycle(
            &mut socket,
            |s| {
                s.open_with_timeout(IpAddress::Ipv4(broker), port, 5000)
                    .is_ok()
            },
            |s| {
                // Wait for the QoS-0 PUBLISH to be ACKed before tearing down.
                // disconnect() sends a RST, so any segment still un-ACKed (or
                // lost on WiFi and not yet retransmitted) would be dropped and
                // the reading silently lost. Bounded so an unreachable broker
                // still can't hang the device.
                let deadline = now() + 5000;
                while s.send_queue() > 0 && now() < deadline {
                    s.work();
                }
                s.disconnect();
            },
            &mqtt::Message {
                client_id: DEVICE_ID,
                topic: &topic,
                payload: payload.as_bytes(),
            },
            || {
                esp_hal::time::Instant::now()
                    .duration_since_epoch()
                    .as_millis()
            },
            5000,
        );
    }

    // Stop WiFi before deep sleep. The radio and its esp-rtos tasks are
    // still running here; letting sleep_deep power things down underneath
    // them can reset the chip (watchdog/crash) instead of sleeping — the
    // device then reboots and publishes a duplicate reading.
    #[cfg(feature = "net")]
    {
        let _ = controller.disconnect();
        let _ = controller.stop();
    }

    // The SSD1315 keeps showing its display RAM as long as it has power and
    // its control lines are held stable, so the value stays on screen
    // through deep sleep. LED off — no point lighting it while asleep.
    led.write([RGB8::new(0, 0, 0)].into_iter()).unwrap();
    delay.delay_millis(10); // WS2812 latch + TCP teardown
    hold_display_pins(true);

    // Deep sleep resets the chip; the next wakeup re-enters main().
    let mut rtc = Rtc::new(peripherals.LPWR);
    let timer = TimerWakeupSource::new(Duration::from_secs(3600));
    rtc.sleep_deep(&[&timer])
}

#[cfg(feature = "net")]
fn smoltcp_now() -> smoltcp::time::Instant {
    smoltcp::time::Instant::from_micros(
        esp_hal::time::Instant::now()
            .duration_since_epoch()
            .as_micros() as i64,
    )
}
