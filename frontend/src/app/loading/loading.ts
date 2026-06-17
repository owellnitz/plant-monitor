import { Component } from '@angular/core';

@Component({
  selector: 'app-loading',
  template: `
    <div class="flex justify-center py-14">
      <span class="loading loading-spinner loading-lg text-base-content/30"></span>
    </div>
  `,
})
export class Loading {}
