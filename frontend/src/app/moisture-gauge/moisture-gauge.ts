import { Component, input } from '@angular/core';
import { WaterStatus } from '../moisture';

/** Radial moisture ring; colored by traffic-light status (neutral when null). Label is projected. */
@Component({
  selector: 'app-moisture-gauge',
  templateUrl: './moisture-gauge.html',
})
export class MoistureGauge {
  readonly percent = input.required<number>();
  readonly status = input<WaterStatus | null>(null);
  readonly size = input('4.5rem');
  readonly thickness = input('5px');
}
