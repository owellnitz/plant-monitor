import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';
import { SensorsPage } from './sensors-page';
import { SensorOverview } from '../sensor';

// rxResource loads in an effect after change detection; a macrotask lets it run.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

function sensor(overrides: Partial<SensorOverview> = {}): SensorOverview {
  return {
    deviceId: 'dev-1',
    raw: 1000,
    percent: 20,
    receivedAt: '2026-06-12T08:00:00Z',
    fw: 'firmware-v0.4.0',
    plantId: null,
    plantName: null,
    ...overrides,
  };
}

async function setup(sensors: SensorOverview[]) {
  const view = await render(SensorsPage, {
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  await tick();
  http.expectOne('/api/sensors').flush(sensors);
  await view.fixture.whenStable();
  return http;
}

describe('SensorsPage', () => {
  it('shows the firmware version and a plant link for an assigned sensor, no delete', async () => {
    const http = await setup([sensor({ plantId: 'p1', plantName: 'Basil' })]);

    expect(screen.getByText('dev-1')).toBeTruthy();
    expect(screen.getByText('Firmware: firmware-v0.4.0')).toBeTruthy();

    const link = screen.getByRole('link', { name: 'Basil' });
    expect(link.getAttribute('href')).toContain('/plant/p1');
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
    http.verify();
  });

  it('falls back to Unknown firmware and offers assign + delete for an unassigned sensor', async () => {
    const http = await setup([sensor({ fw: null })]);

    expect(screen.getByText('Firmware: Unknown')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Assign to plant' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeTruthy();
    http.verify();
  });

  it('confirms, deletes the sensor, then reloads the list', async () => {
    const http = await setup([sensor()]);
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));

    expect(confirmSpy).toHaveBeenCalled();
    const del = http.expectOne('/api/sensors/dev-1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);

    // The list reloads after a successful delete (rxResource refetches on a tick).
    await tick();
    http.expectOne('/api/sensors').flush([]);
    http.verify();
    confirmSpy.mockRestore();
  });

  it('filters between assigned and unassigned sensors', async () => {
    const http = await setup([
      sensor({ deviceId: 'bound', plantId: 'p1', plantName: 'Basil' }),
      sensor({ deviceId: 'free' }),
    ]);

    // No filter: both show.
    expect(screen.getByText('bound')).toBeTruthy();
    expect(screen.getByText('free')).toBeTruthy();

    await userEvent.click(screen.getByRole('tab', { name: 'unassigned' }));
    expect(screen.queryByText('bound')).toBeNull();
    expect(screen.getByText('free')).toBeTruthy();

    await userEvent.click(screen.getByRole('tab', { name: 'assigned' }));
    expect(screen.getByText('bound')).toBeTruthy();
    expect(screen.queryByText('free')).toBeNull();

    http.verify();
  });

  it('does not delete when the confirmation is dismissed', async () => {
    const http = await setup([sensor()]);
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));

    http.verify(); // no DELETE was issued
    confirmSpy.mockRestore();
  });
});
