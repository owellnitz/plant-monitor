import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Sensor } from '../sensor';
import { isLowMoisture, moistureStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-sensors-page',
  imports: [DatePipe, RouterLink, MoistureGauge],
  templateUrl: './sensors-page.html',
})
export class SensorsPage {
  private readonly api = inject(PlantApi);

  protected readonly sensors = signal<Sensor[]>([]);
  protected readonly isLow = isLowMoisture;
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;

  constructor() {
    this.api.getSensors().subscribe((sensors) => this.sensors.set(sensors));
  }
}
