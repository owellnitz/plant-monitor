import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlantApi } from './plant-api';
import { Reading } from './reading';
import { Sensor } from './sensor';
import { Plant, PlantInput } from './plant';
import { Species } from './species';

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

  it('fetches unassigned sensors', () => {
    let sensors: Sensor[] | undefined;
    api.getUnassignedSensors().subscribe((s) => (sensors = s));

    const payload: Sensor[] = [
      { deviceId: 'new-1', raw: 1000, percent: 20, receivedAt: '2026-06-12T08:00:00Z' },
    ];
    http.expectOne('/api/sensors/unassigned').flush(payload);

    expect(sensors).toEqual(payload);
  });

  it('fetches plants', () => {
    let plants: Plant[] | undefined;
    api.getPlants().subscribe((p) => (plants = p));
    http.expectOne('/api/plants').flush([]);
    expect(plants).toEqual([]);
  });

  it('fetches a single plant', () => {
    api.getPlant('abc').subscribe();
    const req = http.expectOne('/api/plants/abc');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('creates a plant', () => {
    const input: PlantInput = {
      name: 'Basil',
      speciesName: 'Genovese',
      location: 'Kitchen',
      sunExposure: 'Full sun',
      deviceId: 'plant-1',
    };
    api.createPlant(input).subscribe();
    const req = http.expectOne('/api/plants');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(input);
    req.flush({});
  });

  it('updates a plant', () => {
    api
      .updatePlant('abc', {
        name: 'Basil',
        speciesName: null,
        location: null,
        sunExposure: null,
        deviceId: null,
      })
      .subscribe();
    const req = http.expectOne('/api/plants/abc');
    expect(req.request.method).toBe('PUT');
    req.flush({});
  });

  it('deletes a plant', () => {
    api.deletePlant('abc').subscribe();
    const req = http.expectOne('/api/plants/abc');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('fetches species', () => {
    let species: Species[] | undefined;
    api.getSpecies().subscribe((s) => (species = s));
    http.expectOne('/api/species').flush([]);
    expect(species).toEqual([]);
  });
});
