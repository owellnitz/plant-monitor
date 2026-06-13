import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Reading } from './reading';
import { Sensor } from './sensor';
import { Plant, PlantInput } from './plant';
import { Species } from './species';

@Injectable({ providedIn: 'root' })
export class PlantApi {
  private readonly http = inject(HttpClient);

  getUnassignedSensors(): Observable<Sensor[]> {
    return this.http.get<Sensor[]>('/api/sensors/unassigned');
  }

  getReadings(deviceId: string, since: Date): Observable<Reading[]> {
    const params = new HttpParams()
      .set('deviceId', deviceId)
      .set('since', since.toISOString())
      .set('limit', 500);
    return this.http.get<Reading[]>('/api/readings', { params });
  }

  getPlants(): Observable<Plant[]> {
    return this.http.get<Plant[]>('/api/plants');
  }

  getPlant(id: string): Observable<Plant> {
    return this.http.get<Plant>(`/api/plants/${id}`);
  }

  createPlant(input: PlantInput): Observable<Plant> {
    return this.http.post<Plant>('/api/plants', input);
  }

  updatePlant(id: string, input: PlantInput): Observable<Plant> {
    return this.http.put<Plant>(`/api/plants/${id}`, input);
  }

  deletePlant(id: string): Observable<void> {
    return this.http.delete<void>(`/api/plants/${id}`);
  }

  getSpecies(): Observable<Species[]> {
    return this.http.get<Species[]>('/api/species');
  }
}
