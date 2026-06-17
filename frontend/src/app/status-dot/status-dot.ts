import { Component, input } from '@angular/core';
import { WaterStatus } from '../moisture';

/** Small traffic-light dot for a reading's watering status (neutral when null). */
@Component({
  selector: 'app-status-dot',
  template: '',
  host: {
    class: 'inline-block h-2 w-2 rounded-full',
    '[class.bg-error]': "status() === 'must'",
    '[class.bg-warning]': "status() === 'can'",
    '[class.bg-primary]': "status() === 'ok'",
    '[class.bg-base-300]': 'status() === null',
  },
})
export class StatusDot {
  readonly status = input<WaterStatus | null>(null);
}
