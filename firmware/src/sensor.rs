//! Grove capacitive moisture sensor: raw ADC reading -> moisture percentage.

// Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
const RAW_DRY: u16 = 4095;
const RAW_WET: u16 = 3130;

/// Maps a raw ADC reading linearly to 0 % (dry) ..= 100 % (wet).
pub fn moisture_percent(raw: u16) -> u32 {
    let clamped = raw.clamp(RAW_WET, RAW_DRY);
    (RAW_DRY - clamped) as u32 * 100 / (RAW_DRY - RAW_WET) as u32
}

/// Mean of the middle half of a sample burst (sorts the buffer in place).
///
/// Drops the lowest and highest quarter, then averages the rest: as
/// spike-robust as a median, but averaging cancels the ADC's random
/// sample-to-sample noise instead of passing one raw sample through.
pub fn trimmed_mean(samples: &mut [u16]) -> u16 {
    samples.sort_unstable();
    let quarter = samples.len() / 4;
    let kept = &samples[quarter..samples.len() - quarter];
    let sum: u32 = kept.iter().map(|&s| u32::from(s)).sum();
    let count = kept.len() as u32;
    ((sum + count / 2) / count) as u16
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
    fn trimmed_mean_of_unsorted_buffer() {
        let mut samples = [3600, 3400, 3500, 3500];
        // Sorted: 3400 [3500 3500] 3600 — extremes dropped, middle averaged.
        assert_eq!(trimmed_mean(&mut samples), 3500);
    }

    #[test]
    fn trimmed_mean_ignores_outlier_spikes() {
        let mut samples = [3500, 3501, 4095, 3499, 0, 3500, 3502, 3500];
        // 0 and 4095 land in the dropped quarters.
        assert_eq!(trimmed_mean(&mut samples), 3500);
    }

    #[test]
    fn trimmed_mean_averages_the_middle_half() {
        let mut samples = [3000, 3400, 3500, 3600, 3700, 4000, 2000, 4095];
        // Sorted: 2000 3000 [3400 3500 3600 3700] 4000 4095 — mean 3550.
        assert_eq!(trimmed_mean(&mut samples), 3550);
    }

    #[test]
    fn trimmed_mean_rounds_to_nearest() {
        let mut samples = [3500, 3501, 3501, 3501];
        // Middle half: 3501, 3501 — exact. Rounding case:
        assert_eq!(trimmed_mean(&mut samples), 3501);
        let mut samples = [3000, 3500, 3501, 4000];
        // Middle half: 3500, 3501 — mean 3500.5 rounds up.
        assert_eq!(trimmed_mean(&mut samples), 3501);
    }
}
