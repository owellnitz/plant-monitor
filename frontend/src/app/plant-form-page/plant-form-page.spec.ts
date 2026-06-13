import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';
import { PlantFormPage } from './plant-form-page';

@Component({ template: '' })
class Blank {}

// rxResource loads in an effect after change detection; a macrotask lets it run.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

describe('PlantFormPage', () => {
  async function setup() {
    const view = await render(PlantFormPage, {
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: Blank }]),
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    // Initial loads for the species and sensor selects.
    await tick();
    http.expectOne('/api/species').flush([]);
    http.expectOne('/api/sensors/unassigned').flush([]);
    await view.fixture.whenStable();
    return { view, http };
  }

  it('sends a typed-in species name when adding a new species', async () => {
    const user = userEvent.setup();
    const { view, http } = await setup();

    await user.type(screen.getByPlaceholderText('Kitchen basil'), 'My basil');
    await user.selectOptions(screen.getByLabelText('Species'), '__new__');
    view.detectChanges();
    await user.type(screen.getByPlaceholderText('Genovese basil'), 'Genovese basil');

    await user.click(screen.getByRole('button', { name: 'Create plant' }));

    const req = http.expectOne('/api/plants');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('My basil');
    expect(req.request.body.speciesName).toBe('Genovese basil');
    req.flush({ id: 'p1' });
    http.verify();
  });
});
