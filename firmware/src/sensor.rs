//! Grove capacitive moisture sensor: raw ADC reading -> moisture percentage.

/// Raw ADC endpoints of one sensor: `raw_dry` maps to 0 %, `raw_wet` to 100 %.
/// Per device, from the `config` partition (see `bin/calibrate.rs`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Calibration {
    pub raw_dry: u16,
    pub raw_wet: u16,
}

impl Calibration {
    /// Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
    /// Used when the config partition has no (valid) calibration.
    pub const DEFAULT: Calibration = Calibration {
        raw_dry: 4095,
        raw_wet: 3130,
    };

    /// `None` unless dry is a value the ADC can report and at least `MIN_DRY`
    /// (anything lower pins every real reading to 0 %), and wet sits at least
    /// `MIN_SPAN` below it: the sensor reads lower the wetter it is, and a
    /// narrower scale means both values were taken in the same medium.
    pub fn new(raw_dry: u16, raw_wet: u16) -> Option<Calibration> {
        ((MIN_DRY..=ADC_MAX).contains(&raw_dry) && raw_dry.saturating_sub(raw_wet) >= MIN_SPAN)
            .then_some(Calibration { raw_dry, raw_wet })
    }
}

/// Lowest raw value accepted as the dry endpoint: half the ADC range, far
/// below any real air or dry-soil reading.
pub const MIN_DRY: u16 = ADC_MAX / 2;

/// Smallest dry-to-wet span accepted as a calibration. Air to water spans
/// ~1000 counts; a few counts of drift within one medium stay far below this.
pub const MIN_SPAN: u16 = 100;

/// Samples per measurement burst and the gap between them. Shared with the
/// calibrate tool so calibration measures like production.
pub const BURST_SAMPLES: usize = 64;
pub const BURST_GAP_MS: u32 = 5;

/// Maps a raw ADC reading linearly to 0 % (dry) ..= 100 % (wet).
pub fn moisture_percent(raw: u16, cal: Calibration) -> u32 {
    let clamped = raw.clamp(cal.raw_wet, cal.raw_dry);
    (cal.raw_dry - clamped) as u32 * 100 / (cal.raw_dry - cal.raw_wet) as u32
}

/// Mean of the middle half of a sample burst (sorts the buffer in place).
///
/// Drops the lowest and highest quarter, then averages the rest: as
/// spike-robust as a median, but averaging cancels the ADC's random
/// sample-to-sample noise instead of passing one raw sample through.
pub fn trimmed_mean(samples: &mut [u16]) -> u16 {
    samples.sort_unstable();
    let quarter = samples.len() / 4;
    rounded_mean(&samples[quarter..samples.len() - quarter])
}

/// Mean of `values`, rounded to nearest. `values` must not be empty.
fn rounded_mean(values: &[u16]) -> u16 {
    let sum: u32 = values.iter().map(|&v| u32::from(v)).sum();
    let count = values.len() as u32;
    ((sum + count / 2) / count) as u16
}

/// Highest raw value the 12-bit ADC can report: a reading here is clipped.
pub const ADC_MAX: u16 = 4095;

/// Rolling window for the calibrate tool: one burst value per second over
/// the last 30 s. Settled = the whole window is within `MAX_SPREAD` counts.
const WINDOW: usize = 30;
const MAX_SPREAD: u16 = 10;

/// One step of [`Calibrator::push`].
pub struct WindowStats {
    pub mean: u16,
    pub spread: u16,
    pub settled: bool,
}

/// Rolling window of burst values for one calibration step (air or water).
#[derive(Default)]
pub struct Calibrator {
    window: heapless::HistoryBuf<u16, WINDOW>,
}

impl Calibrator {
    pub fn push(&mut self, raw: u16) -> WindowStats {
        self.window.write(raw);
        let values = self.window.as_slice();
        let mean = rounded_mean(values);
        let spread = values.iter().max().unwrap() - values.iter().min().unwrap();
        let settled = self.window.is_full() && spread <= MAX_SPREAD;
        WindowStats {
            mean,
            spread,
            settled,
        }
    }

    /// Starts the next step with an empty window, so it must settle anew.
    pub fn reset(&mut self) {
        self.window.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const RAW_DRY: u16 = Calibration::DEFAULT.raw_dry;
    const RAW_WET: u16 = Calibration::DEFAULT.raw_wet;

    fn moisture_percent(raw: u16) -> u32 {
        super::moisture_percent(raw, Calibration::DEFAULT)
    }

    #[test]
    fn custom_calibration_maps_its_own_endpoints() {
        let cal = Calibration::new(3800, 1800).unwrap();
        assert_eq!(super::moisture_percent(3800, cal), 0);
        assert_eq!(super::moisture_percent(2800, cal), 50);
        assert_eq!(super::moisture_percent(1800, cal), 100);
    }

    #[test]
    fn calibration_rejects_wet_not_well_below_dry() {
        assert_eq!(Calibration::new(3000, 3000), None);
        assert_eq!(Calibration::new(3000, 3500), None);
        assert_eq!(Calibration::new(4095, 4095 - MIN_SPAN + 1), None);
        assert!(Calibration::new(4095, 4095 - MIN_SPAN).is_some());
    }

    #[test]
    fn calibration_rejects_dry_below_min_dry() {
        assert_eq!(Calibration::new(150, 0), None);
        assert_eq!(Calibration::new(MIN_DRY - 1, 0), None);
        assert!(Calibration::new(MIN_DRY, 0).is_some());
    }

    #[test]
    fn calibration_rejects_dry_above_adc_max() {
        assert_eq!(Calibration::new(ADC_MAX + 1, 3130), None);
    }

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

    #[test]
    fn calibrator_settles_once_the_window_is_full_and_steady() {
        let mut cal = Calibrator::default();
        for _ in 0..WINDOW - 1 {
            assert!(!cal.push(4000).settled);
        }
        let stats = cal.push(4000);
        assert!(stats.settled);
        assert_eq!(stats.mean, 4000);
    }

    #[test]
    fn calibrator_must_settle_again_after_reset() {
        let mut cal = Calibrator::default();
        (0..WINDOW).for_each(|_| _ = cal.push(4095));
        cal.reset();
        assert!(!cal.push(3130).settled);
    }

    #[test]
    fn calibrator_rejects_a_noisy_window() {
        let mut cal = Calibrator::default();
        let settled = (0..WINDOW as u16)
            .map(|i| cal.push(3500 + (i % 2) * (MAX_SPREAD + 1)).settled)
            .any(|s| s);
        assert!(!settled);
    }
}
