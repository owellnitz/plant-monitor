import { Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core';
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
import annotationPlugin, { type AnnotationOptions } from 'chartjs-plugin-annotation';
import { Reading } from '../reading';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Tooltip,
  Filler,
  annotationPlugin,
);

@Component({
  selector: 'app-moisture-chart',
  template: '<canvas #chart></canvas>',
  host: { class: 'block h-full' },
})
export class MoistureChart implements OnDestroy {
  /** Readings newest first, as returned by the API. */
  readonly readings = input<Reading[]>([]);
  /** Moisture % below which watering is urgent; null hides the line. */
  readonly mustWater = input<number | null>(null);
  /** Moisture % below which watering is OK but not urgent; null hides the line. */
  readonly canWater = input<number | null>(null);

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('chart');
  private chart?: Chart<'line'>;

  constructor() {
    effect(() => this.render());
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  private render(): void {
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

    const style = getComputedStyle(canvas);
    const primary = style.getPropertyValue('--color-primary').trim() || '#2d6a4f';
    const ink = style.getPropertyValue('--color-base-content').trim() || '#1b4332';
    const error = style.getPropertyValue('--color-error').trim() || '#c0392b';
    const warning = style.getPropertyValue('--color-warning').trim() || '#d9980f';
    const font = { family: "'Outfit Variable', sans-serif", size: 11 };

    // Colors match the status-dot scheme: must = error, can = warning.
    const annotations = limitAnnotations(this.mustWater(), this.canWater(), error, warning);

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

    if (this.chart) {
      // New data for an existing chart: update in place instead of a full
      // teardown — keeps the canvas context and avoids a visible flash. Colors
      // are re-read each render and re-applied here, so a theme switch is
      // reflected without recreating the chart.
      const dataset = this.chart.data.datasets[0];
      this.chart.data.labels = labels;
      dataset.data = data;
      dataset.borderColor = primary;
      dataset.backgroundColor = fill;
      this.chart.options.plugins!.annotation!.annotations = annotations;
      this.chart.update();
      return;
    }

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
        plugins: { legend: { display: false }, annotation: { annotations } },
      },
    });
  }
}

/** A single dashed limit line keyed for the annotation map, or empty when unset. */
function limitLine(
  value: number | null,
  label: string,
  color: string,
): Record<string, AnnotationOptions<'line'>> {
  if (value === null) {
    return {};
  }
  return {
    [label]: {
      type: 'line',
      yMin: value,
      yMax: value,
      borderColor: color,
      borderWidth: 2,
      borderDash: [6, 4],
    },
  };
}

/**
 * Horizontal reference lines at the plant's watering limits, in the same 0-100 %
 * unit as the readings. A null limit (bare sensor, or unset) renders no line.
 */
export function limitAnnotations(
  mustWater: number | null,
  canWater: number | null,
  mustColor: string,
  canColor: string,
): Record<string, AnnotationOptions<'line'>> {
  return {
    ...limitLine(mustWater, 'Must water', mustColor),
    ...limitLine(canWater, 'Can water', canColor),
  };
}
