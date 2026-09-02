export interface SensorOverview {
  deviceId: string;
  raw: number;
  percent: number;
  receivedAt: string;
  fw: string | null;
  plantId: string | null;
  plantName: string | null;
}
