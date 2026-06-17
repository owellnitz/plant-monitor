import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { UnassignedSensorsPage } from './unassigned-sensors-page';
import { Sensor } from '../sensor';

// rxResource loads in an effect after change detection; a macrotask lets it run.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

async function setup(sensors: Sensor[]) {
  const view = await render(UnassignedSensorsPage, {
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  await tick();
  http.expectOne('/api/sensors/unassigned').flush(sensors);
  await view.fixture.whenStable();
  return http;
}

describe('UnassignedSensorsPage', () => {
  it('lists an unassigned sensor with an assign link carrying its deviceId', async () => {
    const http = await setup([
      { deviceId: 'new-1', raw: 1000, percent: 20, receivedAt: '2026-06-12T08:00:00Z' },
    ]);

    expect(screen.getByText('new-1')).toBeTruthy();
    expect(screen.getByText('20%')).toBeTruthy();

    const assign = screen.getByRole('link', { name: 'Assign to plant' });
    expect(assign.getAttribute('href')).toContain('/plant/new');
    expect(assign.getAttribute('href')).toContain('deviceId=new-1');
    http.verify();
  });

  it('shows an empty state when every sensor is assigned', async () => {
    const http = await setup([]);

    expect(screen.getByText('No new sensors')).toBeTruthy();
    http.verify();
  });

  it('shows an error state when loading sensors fails', async () => {
    const view = await render(UnassignedSensorsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);

    await tick();
    http
      .expectOne('/api/sensors/unassigned')
      .flush('fail', { status: 500, statusText: 'Server Error' });
    await view.fixture.whenStable();

    expect(screen.getByText('Couldn’t load sensors')).toBeTruthy();
    http.verify();
  });
});
