/** Traffic-light watering state derived from a plant's optional limits. */
export type WaterStatus = 'must' | 'can' | 'ok';

/**
 * Maps a reading to a plant's traffic-light state. Higher moisture = wetter,
 * so a valid pair has mustWater <= canWater. Either limit may be null (unset);
 * when both are null the plant has no limits and this returns null (neutral).
 */
export function waterStatus(
  percent: number,
  mustWater: number | null,
  canWater: number | null,
): WaterStatus | null {
  if (mustWater !== null && percent < mustWater) return 'must';
  if (canWater !== null && percent < canWater) return 'can';
  if (mustWater !== null || canWater !== null) return 'ok';
  return null;
}

export const WATER_STATUS_LABEL: Record<WaterStatus, string> = {
  must: 'Must water',
  can: 'Can water',
  ok: 'Feeling good',
};
