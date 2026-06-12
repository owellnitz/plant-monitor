/** Below this moisture percentage a plant counts as needing water. */
export const LOW_MOISTURE_PERCENT = 40;

export function isLowMoisture(percent: number): boolean {
  return percent < LOW_MOISTURE_PERCENT;
}
