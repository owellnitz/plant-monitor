import { DatePipe } from '@angular/common';
import { Component, effect, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant } from '../plant';
import { RefreshService } from '../refresh';
import { WaterStatus, WATER_STATUS_LABEL, waterStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { Loading } from '../loading/loading';
import { ErrorState } from '../error-state/error-state';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-plants-page',
  imports: [DatePipe, RouterLink, MoistureGauge, Loading, ErrorState],
  templateUrl: './plants-page.html',
})
export class PlantsPage {
  private readonly api = inject(PlantApi);
  private readonly refresh = inject(RefreshService);

  protected readonly plants = rxResource({
    stream: () => this.api.getPlants(),
    defaultValue: [] as Plant[],
  });

  constructor() {
    // Pull-to-refresh reloads in place: reload() keeps the current list visible
    // (status 'reloading'), where a params change would clear it to the default
    // and re-show the full-page loading spinner.
    effect(() => {
      this.refresh.version();
      this.plants.reload();
    });
  }
  protected readonly statusLabel = WATER_STATUS_LABEL;
  protected readonly timeFormat = READING_TIME_FORMAT;

  protected subtitle(plant: Plant): string {
    return [plant.species, plant.location].filter(Boolean).join(' · ');
  }

  /** Traffic-light status for a plant's latest reading, or null (no reading / no limits). */
  protected status(plant: Plant): WaterStatus | null {
    if (plant.percent === null) return null;
    return waterStatus(plant.percent, plant.mustWaterPercent, plant.canWaterPercent);
  }
}
