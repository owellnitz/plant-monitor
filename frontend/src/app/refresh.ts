import { Injectable, signal } from '@angular/core';

/**
 * App-wide refresh trigger. Pages thread `version()` into their rxResource
 * `params` so a bump re-runs every loader on the current page; `refresh()` is
 * called by the pull-to-refresh gesture.
 */
@Injectable({ providedIn: 'root' })
export class RefreshService {
  private readonly _version = signal(0);
  readonly version = this._version.asReadonly();

  refresh(): void {
    this._version.update((v) => v + 1);
  }
}
