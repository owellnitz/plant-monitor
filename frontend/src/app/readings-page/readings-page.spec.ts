import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { render, screen, RenderResult } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';
import { ReadingsPage } from './readings-page';
import { Reading } from '../reading';

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

describe('ReadingsPage', () => {
  let view: RenderResult<ReadingsPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    view = await render(ReadingsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushInitialRequests(readings: Reading[], sensors: string[] = ['plant-1', 'plant-2']) {
    http.expectOne('/api/sensors').flush(sensors);
    http.expectOne((req) => req.url === '/api/readings').flush(readings);
    view.detectChanges();
  }

  it('lists the sensors in the filter dropdown', () => {
    flushInitialRequests([]);

    const options = screen.getAllByRole('option').map((o) => o.textContent?.trim());
    expect(options).toEqual(['All sensors', 'plant-1', 'plant-2']);
  });

  it('shows readings from the API', () => {
    flushInitialRequests([
      reading({ id: 'a', deviceId: 'plant-1', percent: 55 }),
      reading({ id: 'b', deviceId: 'plant-2', percent: 20, raw: 1500 }),
    ]);

    // Each reading renders twice: mobile card list and desktop table.
    expect(screen.getAllByText('plant-1').length).toBeGreaterThan(0);
    expect(screen.getAllByText('55%').length).toBeGreaterThan(0);
    expect(screen.getAllByText('20%').length).toBeGreaterThan(0);
  });

  it('marks low moisture readings with a warning badge', () => {
    flushInitialRequests([
      reading({ id: 'a', percent: 55 }),
      reading({ id: 'b', percent: 20 }),
    ]);

    const ok = screen.getAllByText('55%');
    const low = screen.getAllByText('20%');
    expect(ok.every((el) => el.classList.contains('badge-primary'))).toBe(true);
    expect(low.every((el) => el.classList.contains('badge-warning'))).toBe(true);
  });

  it('shows an empty state when there are no readings', () => {
    flushInitialRequests([]);

    expect(screen.getAllByText('No readings yet').length).toBeGreaterThan(0);
  });

  it('refetches readings filtered by the selected sensor', async () => {
    flushInitialRequests([reading()]);

    await userEvent.selectOptions(screen.getByRole('combobox'), 'plant-2');

    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('deviceId')).toBe('plant-2');
    req.flush([reading({ id: 'c', deviceId: 'plant-2', percent: 33 })]);
    view.detectChanges();

    expect(screen.getAllByText('plant-2').length).toBeGreaterThan(0);
    expect(screen.getAllByText('33%').length).toBeGreaterThan(0);
  });
});
