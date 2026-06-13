import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Sensor } from '../sensor';
import { isLowMoisture } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-unassigned-sensors-page',
  imports: [DatePipe, RouterLink, MoistureGauge],
  templateUrl: './unassigned-sensors-page.html',
})
export class UnassignedSensorsPage {
  private readonly api = inject(PlantApi);

  protected readonly sensors = signal<Sensor[]>([]);
  protected readonly isLow = isLowMoisture;
  protected readonly timeFormat = READING_TIME_FORMAT;

  constructor() {
    this.api.getUnassignedSensors().subscribe((sensors) => this.sensors.set(sensors));
  }
}
