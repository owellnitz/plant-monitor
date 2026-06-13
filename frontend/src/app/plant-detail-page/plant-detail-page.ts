import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant } from '../plant';
import { Reading } from '../reading';
import { isLowMoisture, moistureStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { MoistureChart } from '../moisture-chart/moisture-chart';
import { READING_TIME_FORMAT } from '../format';

const CHART_DAYS = 7;

@Component({
  selector: 'app-plant-detail-page',
  imports: [DatePipe, RouterLink, MoistureGauge, MoistureChart],
  templateUrl: './plant-detail-page.html',
})
export class PlantDetailPage {
  private readonly api = inject(PlantApi);
  private readonly router = inject(Router);

  /** Route param, bound via withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly plant = signal<Plant | undefined>(undefined);
  protected readonly readings = signal<Reading[]>([]);
  protected readonly recent = computed(() => this.readings().slice(0, 10));
  protected readonly isLow = isLowMoisture;
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;
  protected readonly chartDays = CHART_DAYS;

  constructor() {
    effect((onCleanup) => {
      // Clear first so an id change never shows the previous plant's data.
      this.plant.set(undefined);
      this.readings.set([]);
      const sub = this.api.getPlant(this.id()).subscribe((plant) => {
        this.plant.set(plant);
        if (plant.deviceId) {
          const since = new Date(Date.now() - CHART_DAYS * 24 * 60 * 60 * 1000);
          this.api
            .getReadings(plant.deviceId, since)
            .subscribe((readings) => this.readings.set(readings));
        }
      });
      onCleanup(() => sub.unsubscribe());
    });
  }

  protected facts(plant: Plant): { label: string; value: string }[] {
    return [
      { label: 'Species', value: plant.species ?? '—' },
      { label: 'Location', value: plant.location ?? '—' },
      { label: 'Sun', value: plant.sunExposure ?? '—' },
    ];
  }

  protected remove(): void {
    const plant = this.plant();
    if (!plant || !confirm(`Delete ${plant.name}?`)) {
      return;
    }
    this.api.deletePlant(plant.id).subscribe(() => this.router.navigateByUrl('/'));
  }
}
