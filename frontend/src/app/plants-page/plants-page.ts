import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Plant } from '../plant';
import { isLowMoisture, moistureStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-plants-page',
  imports: [DatePipe, RouterLink, MoistureGauge],
  templateUrl: './plants-page.html',
})
export class PlantsPage {
  private readonly api = inject(PlantApi);

  // toSignal subscribes to the HTTP call and unsubscribes on destroy for us;
  // initialValue gives plants() a value before the response arrives.
  protected readonly plants = toSignal(this.api.getPlants(), { initialValue: [] as Plant[] });
  protected readonly isLow = isLowMoisture;
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;

  protected subtitle(plant: Plant): string {
    return [plant.species, plant.location].filter(Boolean).join(' · ');
  }
}
