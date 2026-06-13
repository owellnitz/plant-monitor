import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { PlantsPage } from './plants-page';
import { Plant } from '../plant';

function plant(overrides: Partial<Plant> = {}): Plant {
  return {
    id: 'p1',
    name: 'Kitchen basil',
    species: 'Basil',
    location: 'Kitchen',
    sunExposure: 'Full sun',
    deviceId: 'plant-1',
    percent: 55,
    raw: 3000,
    receivedAt: '2026-06-12T08:00:00Z',
    ...overrides,
  };
}

describe('PlantsPage', () => {
  it('renders a plant with its species and location', async () => {
    const view = await render(PlantsPage, {
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/plants').flush([plant()]);
    view.detectChanges();

    expect(screen.getByText('Kitchen basil')).toBeTruthy();
    expect(screen.getByText('Basil · Kitchen')).toBeTruthy();
    expect(screen.getByText('55%')).toBeTruthy();
    http.verify();
  });
});
