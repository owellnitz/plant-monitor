//! What the OLED shows, as plain text.
//!
//! The renderer centres a block of **exactly two** lines of `FONT_10X20` on
//! the 64 px display (see the `show!` macro in `main.rs`), so a screen with
//! any other number of lines draws off centre. Building the strings here
//! rather than at the call sites keeps that invariant testable on the host,
//! where the display itself cannot run.

use core::fmt::Write as _;

/// One screen's worth of text: two lines separated by a newline.
pub type Screen = heapless::String<64>;

/// Shown while the sensor rail settles, before the ADC burst.
pub fn settling(frame: char) -> Screen {
    let mut text = Screen::new();
    let _ = write!(text, "Reading\n{frame}");
    text
}

/// The reading itself, shown for the rest of the hour.
pub fn reading(percent: u32) -> Screen {
    let mut text = Screen::new();
    let _ = write!(text, "Moisture\n{percent}%");
    text
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every screen the firmware can draw.
    fn every_screen() -> impl Iterator<Item = Screen> {
        ['|', '/', '-', '\\']
            .into_iter()
            .map(settling)
            .chain([0, 7, 62, 100, u32::MAX].into_iter().map(reading))
    }

    // The renderer's baseline is derived from a two-line block, so a third
    // line would draw off centre and a single line too low.
    #[test]
    fn every_screen_is_two_lines() {
        for screen in every_screen() {
            assert_eq!(screen.lines().count(), 2, "screen: {screen:?}");
        }
    }

    // heapless truncates silently on overflow, which would corrupt a screen
    // rather than fail; nothing we render may come close to the cap.
    #[test]
    fn every_screen_fits_its_buffer() {
        for screen in every_screen() {
            assert!(screen.len() < 64, "screen: {screen:?}");
        }
    }

    #[test]
    fn settling_shows_the_spinner_frame() {
        assert_eq!(settling('/').as_str(), "Reading\n/");
    }

    #[test]
    fn reading_shows_the_percentage() {
        assert_eq!(reading(62).as_str(), "Moisture\n62%");
    }
}
