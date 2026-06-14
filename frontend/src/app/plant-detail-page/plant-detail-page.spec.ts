import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { PlantDetailPage } from './plant-detail-page';
import { Plant } from '../plant';

function plant(overrides: Partial<Plant> = {}): Plant {
  return {
    id: 'p1',
    name: 'Kitchen basil',
    species: 'Basil',
    location: 'Kitchen',
    sunExposure: 'Full sun',
    deviceId: null,
    mustWaterPercent: null,
    canWaterPercent: null,
    percent: null,
    raw: null,
    receivedAt: null,
    ...overrides,
  };
}

// rxResource loads in an effect scheduled after change detection; a macrotask
// lets it run. whenStable() can't be used here — a loading resource is a
// pending task, so it would block on the mock request we haven't flushed yet.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

async function setup(p: Plant) {
  const view = await render(PlantDetailPage, {
    inputs: { id: 'p1' },
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const http = TestBed.inject(HttpTestingController);
  await tick();
  http.expectOne('/api/plants/p1').flush(p);
  await tick(); // lets the readings resource react to the loaded deviceId
  view.detectChanges();
  return { view, http };
}

describe('PlantDetailPage', () => {
  it('shows the plant facts', async () => {
    const { http } = await setup(plant());

    expect(screen.getByText('Kitchen basil')).toBeTruthy();
    expect(screen.getByText('Basil')).toBeTruthy();
    expect(screen.getByText('Kitchen')).toBeTruthy();
    expect(screen.getByText('Full sun')).toBeTruthy();
    http.verify();
  });

  it('prompts to assign a sensor when none is bound', async () => {
    const { http } = await setup(plant({ deviceId: null }));

    expect(screen.getByText('No sensor assigned')).toBeTruthy();
    // No sensor → no readings request is made.
    http.verify();
  });

  it('fetches readings for the bound sensor', async () => {
    const { view, http } = await setup(plant({ deviceId: 'plant-1' }));

    // The readings resource fires once the plant resolves with a deviceId.
    const req = http.expectOne((r) => r.url === '/api/readings');
    expect(req.request.params.get('deviceId')).toBe('plant-1');
    req.flush([]); // empty → no chart rendered, avoids canvas in jsdom
    await tick();
    http.verify();
  });
});
