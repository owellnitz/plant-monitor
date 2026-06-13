import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
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

  protected readonly plants = signal<Plant[]>([]);
  protected readonly isLow = isLowMoisture;
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;

  constructor() {
    this.api.getPlants().subscribe((plants) => this.plants.set(plants));
  }

  protected subtitle(plant: Plant): string {
    return [plant.species, plant.location].filter(Boolean).join(' · ');
  }
}
