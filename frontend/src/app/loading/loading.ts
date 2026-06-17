import { Component, input } from '@angular/core';

@Component({
  selector: 'app-loading',
  template: `
    <div class="flex items-center justify-center" [style.min-height]="minHeight()">
      <span class="loading loading-spinner loading-lg text-base-content/30"></span>
    </div>
  `,
})
export class Loading {
  /** Min-height of the centering box; callers reserve space to avoid a layout jump. */
  readonly minHeight = input('8rem');
}
