import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Reading } from '../reading';
import { isLowMoisture, moistureStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { MoistureChart } from '../moisture-chart/moisture-chart';
import { READING_TIME_FORMAT } from '../format';

const CHART_DAYS = 7;

@Component({
  selector: 'app-sensor-detail-page',
  imports: [DatePipe, RouterLink, MoistureGauge, MoistureChart],
  templateUrl: './sensor-detail-page.html',
})
export class SensorDetailPage {
  private readonly api = inject(PlantApi);

  /** Route param, bound via withComponentInputBinding. */
  readonly deviceId = input.required<string>();

  protected readonly readings = signal<Reading[]>([]);
  protected readonly latest = computed<Reading | undefined>(() => this.readings()[0]);
  protected readonly recent = computed(() => this.readings().slice(0, 10));
  protected readonly isLow = isLowMoisture;
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;
  protected readonly chartDays = CHART_DAYS;

  constructor() {
    effect((onCleanup) => {
      // Clear first so a deviceId change never shows the previous sensor's
      // data under the new sensor's heading while the fetch is in flight.
      this.readings.set([]);
      const since = new Date(Date.now() - CHART_DAYS * 24 * 60 * 60 * 1000);
      const subscription = this.api
        .getReadings(this.deviceId(), since)
        .subscribe((readings) => this.readings.set(readings));
      // A deviceId change re-runs the effect; drop the in-flight request so
      // a late response can't overwrite the new sensor's readings.
      onCleanup(() => subscription.unsubscribe());
    });
  }
}
