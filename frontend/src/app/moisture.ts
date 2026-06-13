/** Below this moisture percentage a plant counts as needing water. */
export const LOW_MOISTURE_PERCENT = 40;

export function isLowMoisture(percent: number): boolean {
  return percent < LOW_MOISTURE_PERCENT;
}

/** Canonical status wording, shared by every view that shows plant state. */
export function moistureStatus(percent: number): 'Needs water' | 'Feeling good' {
  return isLowMoisture(percent) ? 'Needs water' : 'Feeling good';
}
