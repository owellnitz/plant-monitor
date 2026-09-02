export interface Reading {
  id: string;
  deviceId: string;
  raw: number;
  percent: number;
  receivedAt: string;
  fw: string | null;
}
