//! Grove capacitive moisture sensor: raw ADC reading -> moisture percentage.

// Calibrated 2026-06-10: 4095 = dry in air (ADC clipped), 3130 = in water.
const RAW_DRY: u16 = 4095;
const RAW_WET: u16 = 3130;

/// Maps a raw ADC reading linearly to 0 % (dry) ..= 100 % (wet).
pub fn moisture_percent(raw: u16) -> u32 {
    let clamped = raw.clamp(RAW_WET, RAW_DRY);
    (RAW_DRY - clamped) as u32 * 100 / (RAW_DRY - RAW_WET) as u32
}
