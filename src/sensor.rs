//! Grove capacitive moisture sensor: raw ADC reading -> moisture percentage.

// Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
const RAW_DRY: u16 = 4095;
const RAW_WET: u16 = 3130;

/// Maps a raw ADC reading linearly to 0 % (dry) ..= 100 % (wet).
pub fn moisture_percent(raw: u16) -> u32 {
    let clamped = raw.clamp(RAW_WET, RAW_DRY);
    (RAW_DRY - clamped) as u32 * 100 / (RAW_DRY - RAW_WET) as u32
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
}
