import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { PlantInput } from '../plant';
import { Sensor } from '../sensor';
import { Species, SUN_EXPOSURES } from '../species';

/** Sentinel for the "add a new species" option in the species select. */
const NEW_SPECIES = '__new__';

@Component({
  selector: 'app-plant-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './plant-form-page.html',
})
export class PlantFormPage {
  private readonly api = inject(PlantApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Present on the edit route (/plant/:id/edit), absent on /plant/new. */
  readonly id = input<string>();

  protected readonly newSpecies = NEW_SPECIES;
  protected readonly sunExposures = SUN_EXPOSURES;
  protected readonly error = signal<string | null>(null);
  protected readonly editing = computed(() => !!this.id());

  protected readonly species = rxResource({
    stream: () => this.api.getSpecies(),
    defaultValue: [] as Species[],
  });

  private readonly unassigned = rxResource({
    stream: () => this.api.getUnassignedSensors(),
    defaultValue: [] as Sensor[],
  });

  // Idle on the create route (no id); loads the plant to prefill when editing.
  private readonly editPlant = rxResource({
    params: () => this.id(),
    stream: ({ params: id }) => this.api.getPlant(id),
  });

  // Unassigned sensors, plus the plant's bound sensor (which isn't in that list).
  protected readonly sensorOptions = computed(() => {
    const ids = this.unassigned.value().map((s) => s.deviceId);
    const current = this.editPlant.value()?.deviceId;
    return current && !ids.includes(current) ? [current, ...ids] : ids;
  });

  protected readonly form = this.fb.nonNullable.group(
    {
      name: ['', Validators.required],
      speciesSelect: [''],
      speciesNew: [''],
      location: [''],
      sunExposure: [''],
      deviceId: [''],
      mustWaterPercent: ['', [Validators.min(0), Validators.max(100)]],
      canWaterPercent: ['', [Validators.min(0), Validators.max(100)]],
    },
    { validators: limitOrderValidator },
  );

  constructor() {
    // Create route: prefill the sensor from ?deviceId=.
    const deviceId = this.route.snapshot.queryParamMap.get('deviceId');
    if (!this.id() && deviceId) {
      this.form.patchValue({ deviceId });
    }
    // Edit route: prefill the form once the plant resource resolves.
    effect(() => {
      const plant = this.editPlant.value();
      if (plant) {
        this.form.patchValue({
          name: plant.name,
          speciesSelect: plant.species ?? '',
          location: plant.location ?? '',
          sunExposure: plant.sunExposure ?? '',
          deviceId: plant.deviceId ?? '',
          mustWaterPercent: plant.mustWaterPercent?.toString() ?? '',
          canWaterPercent: plant.canWaterPercent?.toString() ?? '',
        });
      }
    });
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
      mustWaterPercent: blankToNumber(v.mustWaterPercent),
      canWaterPercent: blankToNumber(v.canWaterPercent),
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

// A type="number" control yields a number or null; an empty/blank field is null.
function blankToNumber(value: string | number | null): number | null {
  if (value === null || (typeof value === 'string' && value.trim() === '')) {
    return null;
  }
  return Number(value);
}

/** When both watering limits are set, must-water must not exceed can-water. */
function limitOrderValidator(group: AbstractControl): ValidationErrors | null {
  const must = blankToNumber(group.get('mustWaterPercent')?.value);
  const can = blankToNumber(group.get('canWaterPercent')?.value);
  if (must !== null && can !== null && must > can) {
    return { limitOrder: true };
  }
  return null;
}
