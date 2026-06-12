import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen, RenderResult } from '@testing-library/angular';
import { SensorDetailPage } from './sensor-detail-page';
import { Reading } from '../reading';

// jsdom has no canvas; replace Chart.js with an inert stand-in.
const chartInstances: { config: unknown; destroyed: boolean }[] = [];
vi.mock('chart.js', () => {
  class Chart {
    destroyed = false;
    constructor(
      public canvas: unknown,
      public config: unknown,
    ) {
      chartInstances.push(this);
    }
    static register(): void {}
    destroy(): void {
      this.destroyed = true;
    }
  }
  return {
    Chart,
    CategoryScale: {},
    Filler: {},
    LineController: {},
    LineElement: {},
    LinearScale: {},
    PointElement: {},
    Tooltip: {},
  };
});

function reading(overrides: Partial<Reading> = {}): Reading {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    deviceId: 'plant-1',
    raw: 3000,
    percent: 55,
    receivedAt: '2026-06-12T08:00:00Z',
    ...overrides,
  };
}

describe('SensorDetailPage', () => {
  let view: RenderResult<SensorDetailPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    chartInstances.length = 0;
    view = await render(SensorDetailPage, {
      inputs: { deviceId: 'plant-1' },
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushReadings(readings: Reading[]) {
    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('deviceId')).toBe('plant-1');
    expect(req.request.params.get('since')).toBeTruthy();
    req.flush(readings);
    view.detectChanges();
  }

  it('shows the latest reading prominently', () => {
    flushReadings([
      reading({ id: 'a', percent: 62, raw: 3100 }),
      reading({ id: 'b', percent: 60, raw: 3150 }),
    ]);

    // The latest reading appears in the hero and again in the recent list.
    expect(screen.getAllByText('62%').length).toBeGreaterThan(0);
    expect(screen.getByText('Feeling good')).toBeTruthy();
  });

  it('marks a dry latest reading as needing water', () => {
    flushReadings([reading({ percent: 12 })]);

    expect(screen.getByText('Needs water')).toBeTruthy();
  });

  it('renders the chart with readings oldest first', async () => {
    flushReadings([
      reading({ id: 'a', percent: 62, receivedAt: '2026-06-12T08:00:00Z' }),
      reading({ id: 'b', percent: 40, receivedAt: '2026-06-11T08:00:00Z' }),
    ]);
    await view.fixture.whenStable();

    expect(chartInstances.length).toBe(1);
    const config = chartInstances[0].config as { data: { datasets: { data: number[] }[] } };
    expect(config.data.datasets[0].data).toEqual([40, 62]);
  });

  it('lists the recent readings', () => {
    flushReadings([
      reading({ id: 'a', percent: 62, raw: 3100 }),
      reading({ id: 'b', percent: 40 }),
    ]);

    expect(screen.getByText('Recent readings')).toBeTruthy();
    expect(screen.getByText('40%')).toBeTruthy();
  });

  it('shows an empty state when the sensor has no readings', () => {
    flushReadings([]);

    expect(screen.getByText('No readings yet')).toBeTruthy();
    expect(chartInstances.length).toBe(0);
  });
});
