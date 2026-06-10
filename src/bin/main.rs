//! Shows the soil moisture reading on a Waveshare 0.96" SPI OLED (SSD1315,
//! SSD1306-compatible), lights the onboard WS2812 RGB LED (GPIO8) of the
//! ESP32-C3-DevKitM-1 and publishes the readings via MQTT.
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

#[cfg(feature = "net")]
use blocking_network_stack::Stack;
use embedded_graphics::{
    mono_font::{MonoTextStyle, ascii::FONT_10X20},
    pixelcolor::BinaryColor,
    prelude::*,
    text::{Alignment, Text},
};
use embedded_hal_bus::spi::ExclusiveDevice;
#[cfg(feature = "net")]
use esp_hal::{
    interrupt::software::SoftwareInterruptControl, rng::Rng, timer::timg::TimerGroup,
};
use esp_hal::{
    analog::adc::{Adc, AdcConfig, Attenuation},
    clock::CpuClock,
    delay::Delay,
    gpio::{Level, Output, OutputConfig},
    main, ram,
    rmt::Rmt,
    spi::{
        Mode,
        master::{Config as SpiConfig, Spi},
    },
    time::Rate,
};
use esp_hal_smartled::{SmartLedsAdapter, smart_led_buffer};
#[cfg(feature = "net")]
use esp_radio::wifi::{ClientConfig, ModeConfig, PowerSaveMode};
#[cfg(feature = "net")]
use esp32_poc::{
    config::{DEVICE_ID, MQTT_HOST, MQTT_PORT, WIFI_PASSWORD, WIFI_SSID},
    mqtt,
};
use esp32_poc::sensor::moisture_percent;
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

#[main]
fn main() -> ! {
    // WiFi needs max CPU clock and a heap (radio blobs allocate).
    let peripherals = esp_hal::init(esp_hal::Config::default().with_cpu_clock(CpuClock::max()));
    esp_alloc::heap_allocator!(#[ram(reclaimed)] size: 64 * 1024);
    esp_alloc::heap_allocator!(size: 36 * 1024);
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
    let now = || esp_hal::time::Instant::now().duration_since_epoch().as_millis();
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

        show!("WiFi\nconnecting");
        controller.connect().unwrap();
        loop {
            match controller.is_connected() {
                Ok(true) => break,
                Ok(false) => {}
                Err(_) => {
                    // Wrong password, AP not found, etc. — show it and retry.
                    show!("WiFi\nretry...");
                    delay.delay_millis(5000);
                    let _ = controller.connect();
                }
            }
        }

        show!("DHCP...");
        loop {
            stack.work();
            if stack.is_iface_up() {
                break;
            }
        }
    }

    #[cfg(feature = "net")]
    let broker: Ipv4Addr = MQTT_HOST.parse().expect("config: mqtt_host is not an IPv4 address");
    #[cfg(feature = "net")]
    let port: u16 = MQTT_PORT.parse().expect("config: mqtt_port is not a number");

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
        show!("MQTT...");
        socket
            .open(IpAddress::Ipv4(broker), port)
            .expect("Failed to open TCP connection to MQTT broker");
        mqtt::connect(&mut socket, DEVICE_ID).expect("MQTT CONNECT failed");
    }

    loop {
        let raw: u16 = nb::block!(adc.read_oneshot(&mut moisture_pin)).unwrap();
        let percent = moisture_percent(raw);

        show!("Moisture\n{percent}%");

        #[cfg(feature = "net")]
        {
            let mut payload: heapless::String<128> = heapless::String::new();
            write!(payload, r#"{{"id":"{DEVICE_ID}","raw":{raw},"percent":{percent}}}"#).unwrap();

            if mqtt::publish(&mut socket, &topic, payload.as_bytes()).is_err() {
                // Connection dropped — re-open TCP and MQTT, publish again next tick.
                show!("MQTT\nreconnect");
                socket.disconnect();
                if socket.open(IpAddress::Ipv4(broker), port).is_ok() {
                    let _ = mqtt::connect(&mut socket, DEVICE_ID);
                }
            }
        }

        delay.delay_millis(1000);
    }
}

#[cfg(feature = "net")]
fn smoltcp_now() -> smoltcp::time::Instant {
    smoltcp::time::Instant::from_micros(
        esp_hal::time::Instant::now()
            .duration_since_epoch()
            .as_micros() as i64,
    )
}
