import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PlantApi } from '../plant-api';
import { SensorOverview } from '../sensor';
import { RefreshService } from '../refresh';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { Loading } from '../loading/loading';
import { ErrorState } from '../error-state/error-state';
import { READING_TIME_FORMAT } from '../format';

@Component({
  selector: 'app-sensors-page',
  imports: [DatePipe, RouterLink, MoistureGauge, Loading, ErrorState],
  templateUrl: './sensors-page.html',
})
export class SensorsPage {
  private readonly api = inject(PlantApi);
  private readonly refresh = inject(RefreshService);

  protected readonly sensors = rxResource({
    stream: () => this.api.getSensors(),
    defaultValue: [] as SensorOverview[],
  });
  protected readonly timeFormat = READING_TIME_FORMAT;

  protected readonly filters = ['all', 'assigned', 'unassigned'] as const;
  protected readonly filter = signal<(typeof this.filters)[number]>('all');

  protected readonly visible = computed(() => {
    const filter = this.filter();
    return this.sensors
      .value()
      .filter((s) =>
        filter === 'assigned' ? s.plantId : filter === 'unassigned' ? !s.plantId : true,
      );
  });

  constructor() {
    // Pull-to-refresh reloads in place so the list stays visible during refresh.
    effect(() => {
      this.refresh.version();
      this.sensors.reload();
    });
  }

  protected remove(sensor: SensorOverview): void {
    if (!confirm(`Delete sensor ${sensor.deviceId} and all its readings?`)) {
      return;
    }
    this.api.deleteSensor(sensor.deviceId).subscribe(() => this.sensors.reload());
  }
}
