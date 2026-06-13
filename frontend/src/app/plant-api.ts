import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Reading } from './reading';
import { Sensor } from './sensor';

@Injectable({ providedIn: 'root' })
export class PlantApi {
  private readonly http = inject(HttpClient);

  getSensors(): Observable<Sensor[]> {
    return this.http.get<Sensor[]>('/api/sensors');
  }

  getReadings(deviceId: string, since: Date): Observable<Reading[]> {
    const params = new HttpParams()
      .set('deviceId', deviceId)
      .set('since', since.toISOString())
      .set('limit', 500);
    return this.http.get<Reading[]>('/api/readings', { params });
  }
}
