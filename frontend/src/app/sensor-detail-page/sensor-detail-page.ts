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
  type ScriptableContext,
  Tooltip,
} from 'chart.js';
import { PlantApi } from '../plant-api';
import { Reading } from '../reading';
import { isLowMoisture, moistureStatus } from '../moisture';
import { MoistureGauge } from '../moisture-gauge/moisture-gauge';
import { READING_TIME_FORMAT } from '../format';

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
  imports: [DatePipe, RouterLink, MoistureGauge],
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
  protected readonly status = moistureStatus;
  protected readonly timeFormat = READING_TIME_FORMAT;
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
    const labels = points.map((r) =>
      new Date(r.receivedAt).toLocaleString([], {
        weekday: 'short',
        hour: '2-digit',
        minute: '2-digit',
      }),
    );
    const data = points.map((r) => r.percent);

    if (this.chart) {
      // New data for an existing chart: update in place instead of a full
      // teardown — keeps the canvas context and avoids a visible flash.
      this.chart.data.labels = labels;
      this.chart.data.datasets[0].data = data;
      this.chart.update();
      return;
    }

    const style = getComputedStyle(canvas);
    const primary = style.getPropertyValue('--color-primary').trim() || '#2d6a4f';
    const ink = style.getPropertyValue('--color-base-content').trim() || '#1b4332';
    const font = { family: "'Outfit Variable', sans-serif", size: 11 };

    // Soft wash under the line, built per draw from the real chart area so it
    // never depends on pre-layout canvas dimensions. Theme colors are 6-digit
    // hex, so alpha can be appended directly.
    const fill = (context: ScriptableContext<'line'>) => {
      const { ctx, chartArea } = context.chart;
      if (!chartArea) {
        return `${primary}47`;
      }
      const gradient = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
      gradient.addColorStop(0, `${primary}47`);
      gradient.addColorStop(1, `${primary}05`);
      return gradient;
    };

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            data,
            borderColor: primary,
            borderWidth: 2,
            backgroundColor: fill,
            fill: true,
            pointRadius: 0,
            pointHitRadius: 12,
            tension: 0.35,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: {
            min: 0,
            max: 100,
            border: { display: false },
            grid: { color: `${ink}12` },
            ticks: { callback: (value) => `${value}%`, font, color: `${ink}73`, maxTicksLimit: 5 },
          },
          x: {
            border: { display: false },
            grid: { display: false },
            ticks: { autoSkip: true, maxTicksLimit: 7, maxRotation: 0, font, color: `${ink}73` },
          },
        },
        plugins: { legend: { display: false } },
      },
    });
  }
}
