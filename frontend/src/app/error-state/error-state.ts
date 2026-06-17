import { Component, input } from '@angular/core';

/** Shown when a resource fails to load. Pull-to-refresh re-runs the request. */
@Component({
  selector: 'app-error-state',
  template: `
    <div class="rounded-box bg-base-100 px-6 py-14 text-center shadow-sm">
      <div class="text-3xl">⚠️</div>
      <p class="mt-3 font-medium">{{ message() }}</p>
      <p class="mt-1 text-sm text-base-content/50">Pull down to try again.</p>
    </div>
  `,
})
export class ErrorState {
  readonly message = input('Couldn’t load');
}
