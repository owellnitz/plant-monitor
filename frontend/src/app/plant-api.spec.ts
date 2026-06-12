import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlantApi } from './plant-api';
import { Reading } from './reading';
import { Sensor } from './sensor';

describe('PlantApi', () => {
  let api: PlantApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PlantApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('fetches sensors with their latest reading', () => {
    let sensors: Sensor[] | undefined;
    api.getSensors().subscribe((s) => (sensors = s));

    const payload: Sensor[] = [
      { deviceId: 'plant-1', raw: 3000, percent: 55, receivedAt: '2026-06-12T08:00:00Z' },
    ];
    http.expectOne('/api/sensors').flush(payload);

    expect(sensors).toEqual(payload);
  });

  it('fetches readings without a device filter', () => {
    let readings: Reading[] | undefined;
    api.getReadings().subscribe((r) => (readings = r));

    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('limit')).toBe('50');
    expect(req.request.params.has('deviceId')).toBe(false);
    req.flush([]);

    expect(readings).toEqual([]);
  });

  it('passes the device filter as a query param', () => {
    api.getReadings('plant-1').subscribe();

    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('deviceId')).toBe('plant-1');
    req.flush([]);
  });
});
