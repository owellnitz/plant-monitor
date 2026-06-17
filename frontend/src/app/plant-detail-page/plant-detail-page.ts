import { DatePipe } from '@angular/common';
import { Component, effect, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant } from '../plant';
import { Reading } from '../reading';
import { RefreshService } from '../refresh';
import { WaterStatus, WATER_STATUS_LABEL, waterStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { Loading } from '../loading/loading';
import { ReadingsSection, CHART_DAYS } from '../readings-section/readings-section';
import { ErrorState } from '../error-state/error-state';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-plant-detail-page',
  imports: [DatePipe, RouterLink, MoistureGauge, ReadingsSection, Loading, ErrorState],
  templateUrl: './plant-detail-page.html',
})
export class PlantDetailPage {
  private readonly api = inject(PlantApi);
  private readonly router = inject(Router);
  private readonly refresh = inject(RefreshService);

  /** Route param, bound via withComponentInputBinding. */
  readonly id = input.required<string>();

  // Re-fetches when the route id changes, cancelling the previous request — no
  // manual subscription, no stale-response race.
  protected readonly plant = rxResource({
    params: () => this.id(),
    stream: ({ params: id }) => this.api.getPlant(id),
  });

  // Driven by the loaded plant's sensor: undefined params (no sensor) means the
  // resource stays idle and never fetches readings.
  protected readonly readings = rxResource({
    params: () => {
      // hasValue() guards against value() throwing while the plant is loading or
      // errored — otherwise a failed plant load would throw here too.
      const deviceId = this.plant.hasValue() ? this.plant.value()?.deviceId : undefined;
      return deviceId ? { deviceId } : undefined;
    },
    stream: ({ params }) =>
      this.api.getReadings(
        params.deviceId,
        new Date(Date.now() - CHART_DAYS * 24 * 60 * 60 * 1000),
      ),
    defaultValue: [] as Reading[],
  });

  constructor() {
    // Pull-to-refresh reloads in place (status 'reloading') so the loaded plant
    // and chart stay visible during refresh instead of flashing the spinner. The
    // readings reload runs after the plant one so the deviceId is current.
    effect(() => {
      this.refresh.version();
      this.plant.reload();
      this.readings.reload();
    });
  }

  protected readonly statusLabel = WATER_STATUS_LABEL;
  protected readonly timeFormat = READING_TIME_FORMAT;

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
