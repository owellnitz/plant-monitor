import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Sensor } from '../sensor';
import { isLowMoisture } from '../moisture';

@Component({
  selector: 'app-sensors-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './sensors-page.html',
})
export class SensorsPage {
  private readonly api = inject(PlantApi);

  protected readonly sensors = signal<Sensor[]>([]);
  protected readonly isLow = isLowMoisture;

  constructor() {
    this.api.getSensors().subscribe((sensors) => this.sensors.set(sensors));
  }
}
