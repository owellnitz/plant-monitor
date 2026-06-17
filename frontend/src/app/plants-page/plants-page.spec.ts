import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { PlantsPage } from './plants-page';
import { Plant } from '../plant';
import { RefreshService } from '../refresh';

function plant(overrides: Partial<Plant> = {}): Plant {
  return {
    id: 'p1',
    name: 'Kitchen basil',
    species: 'Basil',
    location: 'Kitchen',
    sunExposure: 'Full sun',
    deviceId: 'plant-1',
    mustWaterPercent: null,
    canWaterPercent: null,
    percent: 55,
    raw: 3000,
    receivedAt: '2026-06-12T08:00:00Z',
    ...overrides,
  };
}

// rxResource loads in an effect after change detection; a macrotask lets it run.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

describe('PlantsPage', () => {
  it('renders a plant with its species and location', async () => {
    const view = await render(PlantsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);

    await tick();
    http.expectOne('/api/plants').flush([plant()]);
    await view.fixture.whenStable();

    expect(screen.getByText('Kitchen basil')).toBeTruthy();
    expect(screen.getByText('Basil · Kitchen')).toBeTruthy();
    expect(screen.getByText('55%')).toBeTruthy();
    http.verify();
  });

  it('shows an error state when loading plants fails', async () => {
    const view = await render(PlantsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);

    await tick();
    http.expectOne('/api/plants').flush('fail', { status: 500, statusText: 'Server Error' });
    await view.fixture.whenStable();

    expect(screen.getByText('Couldn’t load plants')).toBeTruthy();
    expect(screen.getByText('Pull down to try again.')).toBeTruthy();
    http.verify();
  });

  it('reloads in place on refresh, keeping the current list visible', async () => {
    const view = await render(PlantsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);

    await tick();
    http.expectOne('/api/plants').flush([plant({ name: 'Aloe' })]);
    await view.fixture.whenStable();
    expect(screen.getByText('Aloe')).toBeTruthy();

    TestBed.inject(RefreshService).refresh();
    await tick();
    // reload() refetches while keeping the resolved data on screen (no spinner).
    expect(screen.getByText('Aloe')).toBeTruthy();

    http.expectOne('/api/plants').flush([plant({ name: 'Thyme' })]);
    await view.fixture.whenStable();
    expect(screen.getByText('Thyme')).toBeTruthy();
    http.verify();
  });
});
