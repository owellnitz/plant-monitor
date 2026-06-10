//! Grove capacitive moisture sensor: raw ADC reading -> moisture percentage.

// Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
const RAW_DRY: u16 = 4095;
const RAW_WET: u16 = 3130;

/// Maps a raw ADC reading linearly to 0 % (dry) ..= 100 % (wet).
pub fn moisture_percent(raw: u16) -> u32 {
    let clamped = raw.clamp(RAW_WET, RAW_DRY);
    (RAW_DRY - clamped) as u32 * 100 / (RAW_DRY - RAW_WET) as u32
}

/// Exponential moving average (alpha = 1/8) over raw ADC readings.
///
/// Smooths reading-to-reading ADC noise. Soil moisture changes over minutes,
/// so the few-readings lag is irrelevant.
pub struct Ema {
    filtered: Option<i32>,
}

impl Ema {
    pub const fn new() -> Self {
        Self { filtered: None }
    }

    /// Feeds one reading, returns the smoothed value.
    /// The first reading passes through unchanged.
    pub fn update(&mut self, raw: u16) -> u16 {
        let f = self.filtered.get_or_insert(raw as i32);
        *f += (raw as i32 - *f) >> 3;
        *f as u16
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dry_is_zero_percent() {
        assert_eq!(moisture_percent(RAW_DRY), 0);
    }

    #[test]
    fn wet_is_hundred_percent() {
        assert_eq!(moisture_percent(RAW_WET), 100);
    }

    #[test]
    fn wetter_than_calibration_clamps_to_hundred() {
        assert_eq!(moisture_percent(0), 100);
        assert_eq!(moisture_percent(RAW_WET - 1), 100);
    }

    #[test]
    fn drier_than_calibration_clamps_to_zero() {
        assert_eq!(moisture_percent(u16::MAX), 0);
    }

    #[test]
    fn midpoint_is_about_fifty_percent() {
        let mid = RAW_WET + (RAW_DRY - RAW_WET) / 2;
        assert_eq!(moisture_percent(mid), 50);
    }

    #[test]
    fn ema_first_reading_passes_through() {
        let mut ema = Ema::new();
        assert_eq!(ema.update(3500), 3500);
    }

    #[test]
    fn ema_damps_spike_to_an_eighth() {
        let mut ema = Ema::new();
        ema.update(3500);
        assert_eq!(ema.update(3600), 3512);
    }

    #[test]
    fn ema_converges_to_steady_input() {
        let mut ema = Ema::new();
        ema.update(RAW_DRY);
        let mut last = 0;
        for _ in 0..100 {
            last = ema.update(RAW_WET);
        }
        assert!(last.abs_diff(RAW_WET) <= 7, "stuck at {last}");
    }
}
