import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { Reading } from '../reading';
import { WaterStatus, waterStatus } from '../moisture';
import { MoistureChart } from '../moisture-chart/moisture-chart';
import { StatusDot } from '../status-dot/status-dot';
import { READING_TIME_FORMAT } from '../format';

/** Days of history the chart shows; also the window callers fetch readings for. */
export const CHART_DAYS = 7;

/**
 * Moisture chart + recent-readings list, shared by the plant and sensor detail
 * pages. Dots are colored against the optional watering limits (both null for a
 * bare sensor, which renders neutral dots). Assumes a non-empty readings array.
 */
@Component({
  selector: 'app-readings-section',
  imports: [DatePipe, MoistureChart, StatusDot],
  templateUrl: './readings-section.html',
})
export class ReadingsSection {
  readonly readings = input<Reading[]>([]);
  readonly mustWater = input<number | null>(null);
  readonly canWater = input<number | null>(null);

  protected readonly chartDays = CHART_DAYS;
  protected readonly recent = computed(() => this.readings().slice(0, 10));
  protected readonly timeFormat = READING_TIME_FORMAT;

  protected status(percent: number): WaterStatus | null {
    return waterStatus(percent, this.mustWater(), this.canWater());
  }
}
