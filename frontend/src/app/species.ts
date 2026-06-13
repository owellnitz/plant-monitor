export interface Species {
  id: string;
  name: string;
}

/** Sun exposure options offered in the plant form. */
export const SUN_EXPOSURES = ['Full sun', 'Partial sun', 'Shade'] as const;
