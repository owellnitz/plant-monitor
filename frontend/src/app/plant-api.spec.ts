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

  it('fetches readings for a device since a date', () => {
    let readings: Reading[] | undefined;
    const since = new Date('2026-06-05T08:00:00Z');
    api.getReadings('plant-1', since).subscribe((r) => (readings = r));

    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('deviceId')).toBe('plant-1');
    expect(req.request.params.get('since')).toBe('2026-06-05T08:00:00.000Z');
    expect(req.request.params.get('limit')).toBe('500');
    req.flush([]);

    expect(readings).toEqual([]);
  });
});
