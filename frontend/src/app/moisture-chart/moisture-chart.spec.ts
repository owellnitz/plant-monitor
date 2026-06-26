import { render } from '@testing-library/angular';
import { MoistureChart } from './moisture-chart';
import { Reading } from '../reading';

// jsdom has no canvas; replace Chart.js with an inert stand-in that captures the
// config so we can assert on the annotation lines built from the limits.
const chartInstances: { config: ChartConfig }[] = [];
vi.mock('chart.js', () => {
  class Chart {
    constructor(
      public canvas: unknown,
      public config: ChartConfig,
    ) {
      chartInstances.push(this);
    }
    static register(): void {}
    destroy(): void {}
  }
  return {
    Chart,
    CategoryScale: {},
    Filler: {},
    LineController: {},
    LineElement: {},
    LinearScale: {},
    PointElement: {},
    Tooltip: {},
  };
});

interface LineAnnotation {
  yMin: number;
  yMax: number;
  borderColor: string;
  label: { content: string };
}
interface ChartConfig {
  options: { plugins: { annotation: { annotations: Record<string, LineAnnotation> } } };
}

function reading(percent: number): Reading {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    deviceId: 'plant-1',
    raw: 3000,
    percent,
    receivedAt: '2026-06-12T08:00:00Z',
  };
}

async function annotationsFor(inputs: {
  mustWater?: number | null;
  canWater?: number | null;
}): Promise<Record<string, LineAnnotation>> {
  chartInstances.length = 0;
  await render(MoistureChart, { inputs: { readings: [reading(50)], ...inputs } });
  return chartInstances[0].config.options.plugins.annotation.annotations;
}

describe('MoistureChart watering levels', () => {
  it('draws a labeled line at each limit', async () => {
    const annotations = await annotationsFor({ mustWater: 30, canWater: 60 });

    expect(annotations['Must water']).toMatchObject({ yMin: 30, yMax: 30 });
    expect(annotations['Can water']).toMatchObject({ yMin: 60, yMax: 60 });
    expect(annotations['Must water'].label.content).toBe('Must water');
    expect(annotations['Can water'].label.content).toBe('Can water');
  });

  it('colors must-water as error and can-water as warning', async () => {
    const annotations = await annotationsFor({ mustWater: 30, canWater: 60 });

    expect(annotations['Must water'].borderColor).toBe('#c0392b');
    expect(annotations['Can water'].borderColor).toBe('#d9980f');
  });

  it('draws only the line for a set limit', async () => {
    const annotations = await annotationsFor({ mustWater: 30, canWater: null });

    expect(annotations['Must water']).toBeTruthy();
    expect(annotations['Can water']).toBeUndefined();
  });

  it('draws no lines when both limits are unset', async () => {
    const annotations = await annotationsFor({ mustWater: null, canWater: null });

    expect(Object.keys(annotations)).toHaveLength(0);
  });
});
