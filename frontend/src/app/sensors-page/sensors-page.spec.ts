import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen, RenderResult } from '@testing-library/angular';
import { SensorsPage } from './sensors-page';
import { Sensor } from '../sensor';

function sensor(overrides: Partial<Sensor> = {}): Sensor {
  return {
    deviceId: 'plant-1',
    raw: 3000,
    percent: 55,
    receivedAt: '2026-06-12T08:00:00Z',
    ...overrides,
  };
}

describe('SensorsPage', () => {
  let view: RenderResult<SensorsPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    view = await render(SensorsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushSensors(sensors: Sensor[]) {
    http.expectOne('/api/sensors').flush(sensors);
    view.detectChanges();
  }

  it('shows a card per sensor with its latest value', () => {
    flushSensors([
      sensor({ deviceId: 'plant-1', percent: 55 }),
      sensor({ deviceId: 'plant-2', percent: 20 }),
    ]);

    expect(screen.getByText('plant-1')).toBeTruthy();
    expect(screen.getByText('55%')).toBeTruthy();
    expect(screen.getByText('plant-2')).toBeTruthy();
    expect(screen.getByText('20%')).toBeTruthy();
  });

  it('links each card to the sensor detail page', () => {
    flushSensors([sensor({ deviceId: 'plant-1' })]);

    const link = screen.getByRole('link') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/sensor/plant-1');
  });

  it('marks low moisture sensors with a warning badge', () => {
    flushSensors([
      sensor({ deviceId: 'plant-1', percent: 55 }),
      sensor({ deviceId: 'plant-2', percent: 20 }),
    ]);

    expect(screen.getByText('55%').classList.contains('badge-primary')).toBe(true);
    expect(screen.getByText('20%').classList.contains('badge-warning')).toBe(true);
  });

  it('shows an empty state when there are no sensors', () => {
    flushSensors([]);

    expect(screen.getByText('No sensors yet')).toBeTruthy();
  });
});
