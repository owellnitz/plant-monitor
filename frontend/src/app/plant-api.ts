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

  getReadings(deviceId?: string): Observable<Reading[]> {
    let params = new HttpParams().set('limit', 50);
    if (deviceId) {
      params = params.set('deviceId', deviceId);
    }
    return this.http.get<Reading[]>('/api/readings', { params });
  }
}
