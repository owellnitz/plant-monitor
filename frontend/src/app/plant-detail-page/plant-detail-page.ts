import { DatePipe } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant } from '../plant';
import { Reading } from '../reading';
import { RefreshService } from '../refresh';
import { WaterStatus, WATER_STATUS_LABEL, waterStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { MoistureChart } from '../moisture-chart/moisture-chart';
import { Loading } from '../loading/loading';
import { StatusDot } from '../status-dot/status-dot';
import { READING_TIME_FORMAT } from '../format';

const CHART_DAYS = 7;

@Component({
  selector: 'app-plant-detail-page',
  imports: [DatePipe, RouterLink, MoistureGauge, MoistureChart, Loading, StatusDot],
  templateUrl: './plant-detail-page.html',
})
export class PlantDetailPage {
  private readonly api = inject(PlantApi);
  private readonly router = inject(Router);
  private readonly refresh = inject(RefreshService);

  /** Route param, bound via withComponentInputBinding. */
  readonly id = input.required<string>();

  // Re-fetches when the route id or the refresh trigger changes, and cancels the
  // previous request — no manual effect/subscription, no stale-response race.
  // The readings resource chains off this, so it reloads too.
  protected readonly plant = rxResource({
    params: () => ({ id: this.id(), version: this.refresh.version() }),
    stream: ({ params }) => this.api.getPlant(params.id),
  });

  // Driven by the loaded plant's sensor: undefined params (no sensor) means the
  // resource stays idle and never fetches readings. The refresh version is
  // included so a pull-to-refresh reloads the chart too (the deviceId is stable).
  protected readonly readings = rxResource({
    params: () => {
      const deviceId = this.plant.value()?.deviceId;
      return deviceId ? { deviceId, version: this.refresh.version() } : undefined;
    },
    stream: ({ params }) =>
      this.api.getReadings(
        params.deviceId,
        new Date(Date.now() - CHART_DAYS * 24 * 60 * 60 * 1000),
      ),
    defaultValue: [] as Reading[],
  });

  protected readonly recent = computed(() => this.readings.value().slice(0, 10));
  protected readonly statusLabel = WATER_STATUS_LABEL;
  protected readonly timeFormat = READING_TIME_FORMAT;
  protected readonly chartDays = CHART_DAYS;

  /** Traffic-light status for a given moisture reading against the loaded plant's limits. */
  protected status(percent: number): WaterStatus | null {
    const plant = this.plant.value();
    return plant ? waterStatus(percent, plant.mustWaterPercent, plant.canWaterPercent) : null;
  }

  protected facts(plant: Plant): { label: string; value: string }[] {
    return [
      { label: 'Species', value: plant.species ?? '—' },
      { label: 'Location', value: plant.location ?? '—' },
      { label: 'Sun', value: plant.sunExposure ?? '—' },
    ];
  }

  protected remove(): void {
    const plant = this.plant.value();
    if (!plant || !confirm(`Delete ${plant.name}?`)) {
      return;
    }
    this.api.deletePlant(plant.id).subscribe(() => this.router.navigateByUrl('/'));
  }
}
