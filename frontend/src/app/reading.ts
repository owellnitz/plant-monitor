export interface Reading {
  id: string;
  deviceId: string;
  raw: number;
  percent: number;
  receivedAt: string;
  fw: string | null;
  /** Why the device booted before this reading; `deep_sleep` is the healthy case. */
  reset: string | null;
  /** What the previous wake's firmware update attempt did. */
  ota: string | null;
}
