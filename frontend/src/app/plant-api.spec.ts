import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlantApi } from './plant-api';
import { Reading } from './reading';

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

  it('fetches sensors', () => {
    let sensors: string[] | undefined;
    api.getSensors().subscribe((s) => (sensors = s));

    http.expectOne('/api/sensors').flush(['plant-1', 'plant-2']);

    expect(sensors).toEqual(['plant-1', 'plant-2']);
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
