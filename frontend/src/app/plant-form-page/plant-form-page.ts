import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant, PlantInput } from '../plant';
import { Species, SUN_EXPOSURES } from '../species';

/** Sentinel for the "add a new species" option in the species select. */
const NEW_SPECIES = '__new__';

@Component({
  selector: 'app-plant-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './plant-form-page.html',
})
export class PlantFormPage implements OnInit {
  private readonly api = inject(PlantApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Present on the edit route (/plant/:id/edit), absent on /plant/new. */
  readonly id = input<string>();

  protected readonly newSpecies = NEW_SPECIES;
  protected readonly sunExposures = SUN_EXPOSURES;
  protected readonly species = signal<Species[]>([]);
  protected readonly sensorOptions = signal<string[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly editing = computed(() => !!this.id());

  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    speciesSelect: [''],
    speciesNew: [''],
    location: [''],
    sunExposure: [''],
    deviceId: [''],
  });

  ngOnInit(): void {
    this.api.getSpecies().subscribe((species) => this.species.set(species));
    this.api
      .getUnassignedSensors()
      .subscribe((sensors) => this.sensorOptions.set(sensors.map((s) => s.deviceId)));

    const id = this.id();
    if (id) {
      this.api.getPlant(id).subscribe((plant) => this.prefill(plant));
    } else {
      const deviceId = this.route.snapshot.queryParamMap.get('deviceId');
      if (deviceId) {
        this.form.patchValue({ deviceId });
      }
    }
  }

  private prefill(plant: Plant): void {
    this.form.patchValue({
      name: plant.name,
      speciesSelect: plant.species ?? '',
      location: plant.location ?? '',
      sunExposure: plant.sunExposure ?? '',
      deviceId: plant.deviceId ?? '',
    });
    // The bound sensor isn't in the unassigned list — add it so it stays selected.
    if (plant.deviceId) {
      this.sensorOptions.update((ids) =>
        ids.includes(plant.deviceId!) ? ids : [plant.deviceId!, ...ids],
      );
    }
  }

  protected save(): void {
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    const input: PlantInput = {
      name: v.name.trim(),
      speciesName:
        v.speciesSelect === NEW_SPECIES ? blankToNull(v.speciesNew) : blankToNull(v.speciesSelect),
      location: blankToNull(v.location),
      sunExposure: blankToNull(v.sunExposure),
      deviceId: blankToNull(v.deviceId),
    };

    const id = this.id();
    const request = id ? this.api.updatePlant(id, input) : this.api.createPlant(input);
    request.subscribe({
      next: (plant) => this.router.navigate(['/plant', plant.id]),
      error: (err) =>
        this.error.set(
          err.status === 409
            ? 'That sensor is already assigned to another plant.'
            : 'Could not save the plant.',
        ),
    });
  }
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}
