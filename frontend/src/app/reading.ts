export interface Reading {
  id: string;
  deviceId: string;
  raw: number;
  percent: number;
  receivedAt: string;
  fw: string | null;
  /** What the previous wake's firmware update attempt did. */
  ota: string | null;
}
