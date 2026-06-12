import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  CategoryScale,
  Chart,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
} from 'chart.js';
import { PlantApi } from '../plant-api';
import { Reading } from '../reading';
import { isLowMoisture } from '../moisture';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Tooltip,
  Filler,
);

const CHART_DAYS = 7;

@Component({
  selector: 'app-sensor-detail-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './sensor-detail-page.html',
})
export class SensorDetailPage implements OnDestroy {
  private readonly api = inject(PlantApi);

  /** Route param, bound via withComponentInputBinding. */
  readonly deviceId = input.required<string>();

  protected readonly readings = signal<Reading[]>([]);
  protected readonly latest = computed<Reading | undefined>(() => this.readings()[0]);
  protected readonly recent = computed(() => this.readings().slice(0, 10));
  protected readonly isLow = isLowMoisture;
  protected readonly chartDays = CHART_DAYS;

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('chart');
  private chart?: Chart;

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
    effect(() => this.renderChart());
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  private renderChart(): void {
    const canvas = this.canvas()?.nativeElement;
    const readings = this.readings();
    if (!canvas || readings.length === 0) {
      return;
    }

    // API returns newest first; the chart runs left to right in time.
    const points = [...readings].reverse();
    const primary = getComputedStyle(canvas).getPropertyValue('--color-primary') || '#2d6a4f';

    this.chart?.destroy();
    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: points.map((r) =>
          new Date(r.receivedAt).toLocaleString([], {
            weekday: 'short',
            hour: '2-digit',
            minute: '2-digit',
          }),
        ),
        datasets: [
          {
            data: points.map((r) => r.percent),
            borderColor: primary,
            backgroundColor: primary,
            pointRadius: 0,
            tension: 0.3,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: { min: 0, max: 100, ticks: { callback: (value) => `${value}%` } },
          x: { ticks: { autoSkip: true, maxTicksLimit: 7, maxRotation: 0 } },
        },
        plugins: { legend: { display: false } },
      },
    });
  }
}
