export interface Plant {
  id: string;
  name: string;
  species: string | null;
  location: string | null;
  sunExposure: string | null;
  deviceId: string | null;
  mustWaterPercent: number | null;
  canWaterPercent: number | null;
  percent: number | null;
  raw: number | null;
  receivedAt: string | null;
}

/** Body for creating or updating a plant; speciesName is upserted by name. */
export interface PlantInput {
  name: string;
  speciesName: string | null;
  location: string | null;
  sunExposure: string | null;
  deviceId: string | null;
  mustWaterPercent: number | null;
  canWaterPercent: number | null;
}
