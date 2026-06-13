import { Component, input } from '@angular/core';
import { isLowMoisture } from '../moisture';

/** Radial moisture ring: green when ok, amber when dry. Label is projected. */
@Component({
  selector: 'app-moisture-gauge',
  templateUrl: './moisture-gauge.html',
})
export class MoistureGauge {
  readonly percent = input.required<number>();
  readonly size = input('4.5rem');
  readonly thickness = input('5px');

  protected readonly isLow = isLowMoisture;
}
