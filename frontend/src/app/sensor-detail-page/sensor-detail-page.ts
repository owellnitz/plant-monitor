import { DatePipe } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Reading } from '../reading';
import { RefreshService } from '../refresh';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { Loading } from '../loading/loading';
import { ReadingsSection, CHART_DAYS } from '../readings-section/readings-section';
import { ErrorState } from '../error-state/error-state';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-sensor-detail-page',
  imports: [DatePipe, RouterLink, MoistureGauge, ReadingsSection, Loading, ErrorState],
  templateUrl: './sensor-detail-page.html',
})
export class SensorDetailPage {
  private readonly api = inject(PlantApi);
  private readonly refresh = inject(RefreshService);

  /** Route param, bound via withComponentInputBinding. */
  readonly deviceId = input.required<string>();

  // Re-fetches when the route deviceId or the refresh trigger changes, and
  // cancels the previous request — no manual effect/subscription, no race.
  protected readonly readings = rxResource({
    params: () => ({ deviceId: this.deviceId(), version: this.refresh.version() }),
    stream: ({ params }) =>
      this.api.getReadings(
        params.deviceId,
        new Date(Date.now() - CHART_DAYS * 24 * 60 * 60 * 1000),
      ),
    defaultValue: [] as Reading[],
  });

  protected readonly latest = computed<Reading | undefined>(() => this.readings.value()[0]);
  protected readonly timeFormat = READING_TIME_FORMAT;
}
