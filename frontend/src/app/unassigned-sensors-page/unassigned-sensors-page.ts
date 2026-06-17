import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { Sensor } from '../sensor';
import { RefreshService } from '../refresh';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { Loading } from '../loading/loading';
import { ErrorState } from '../error-state/error-state';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-unassigned-sensors-page',
  imports: [DatePipe, RouterLink, MoistureGauge, Loading, ErrorState],
  templateUrl: './unassigned-sensors-page.html',
})
export class UnassignedSensorsPage {
  private readonly api = inject(PlantApi);
  private readonly refresh = inject(RefreshService);

  protected readonly sensors = rxResource({
    params: () => this.refresh.version(),
    stream: () => this.api.getUnassignedSensors(),
    defaultValue: [] as Sensor[],
  });
  protected readonly timeFormat = READING_TIME_FORMAT;
}
