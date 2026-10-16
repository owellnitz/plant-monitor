import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { Loading } from '../loading/loading';
import { PlantApi } from '../plant-api';

/**
 * checking    - reading the browser's and the backend's state
 * unsupported - no service worker or Push API (dev build, non-HTTPS origin, iOS outside the Home Screen)
 * unavailable - the deployment has no VAPID keys configured
 * denied      - the browser refused; iOS only asks again after a reinstall
 */
type PushState = 'checking' | 'unsupported' | 'unavailable' | 'denied' | 'off' | 'on';

@Component({
  selector: 'app-settings-page',
  imports: [Loading, RouterLink],
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(PlantApi);
  private readonly swPush = inject(SwPush);
  private publicKey = '';

  protected readonly state = signal<PushState>('checking');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly testResult = signal('');

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    if (!this.swPush.isEnabled || !('PushManager' in window)) {
      this.state.set('unsupported');
      return;
    }

    try {
      this.publicKey = (await firstValueFrom(this.api.getVapidKey())).publicKey;
    } catch {
      this.state.set('unavailable');
      return;
    }

    if (permissionDenied()) {
      this.state.set('denied');
      return;
    }

    const subscription = await firstValueFrom(this.swPush.subscription);
    if (subscription) {
      // Re-register: the backend may have pruned the row, or never stored it after a failed POST.
      await this.register(subscription).catch(() =>
        this.error.set('Couldn’t confirm this device with the server.'),
      );
    }
    this.state.set(subscription ? 'on' : 'off');
  }

  /**
   * A notification only fires on a threshold crossing, which may be days away.
   * This is the only way to tell a setup that never worked from one that has
   * simply had nothing to report yet.
   */
  protected async sendTest(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.testResult.set('');
    try {
      const subscription = await firstValueFrom(this.swPush.subscription);
      const { delivered } = await firstValueFrom(
        this.api.sendTestPush(subscription?.endpoint ?? ''),
      );
      this.testResult.set(
        delivered
          ? 'Sent to this device.'
          : 'This device’s push service didn’t take it — try switching notifications off and on again.',
      );
    } catch (e) {
      this.error.set(
        e instanceof HttpErrorResponse && e.status === 404
          ? 'The server doesn’t know this device — switch notifications off and on again.'
          : 'Couldn’t send the test notification.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(event: Event): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.testResult.set('');
    try {
      await (this.state() === 'on' ? this.disable() : this.enable());
    } catch {
      // iOS gives no second prompt once denied — say so rather than retry.
      if (permissionDenied()) {
        this.state.set('denied');
      } else {
        this.error.set('Couldn’t change the notification setting.');
      }
    } finally {
      this.busy.set(false);
      // The browser already flipped the box; a failed toggle must flip it back.
      (event.target as HTMLInputElement).checked = this.state() === 'on';
    }
  }

  private async enable(): Promise<void> {
    // Must run straight off the click: iOS only shows the permission prompt
    // for a user gesture.
    const subscription = await this.swPush.requestSubscription({ serverPublicKey: this.publicKey });
    await this.register(subscription);
    this.state.set('on');
  }

  private register(subscription: PushSubscription): Promise<void> {
    const { endpoint, keys } = subscription.toJSON();
    return firstValueFrom(
      this.api.subscribePush({
        endpoint: endpoint ?? '',
        p256dh: keys?.['p256dh'] ?? '',
        auth: keys?.['auth'] ?? '',
      }),
    );
  }

  private async disable(): Promise<void> {
    // Read the endpoint before unsubscribing — afterwards it is gone.
    const subscription = await firstValueFrom(this.swPush.subscription);
    await this.swPush.unsubscribe();

    if (subscription) {
      // Any failure is fine: 404 means gone, else the backend prunes it on its next send.
      await firstValueFrom(this.api.unsubscribePush(subscription.endpoint)).catch(() => undefined);
    }
    this.state.set('off');
  }
}

// iOS Safari outside the Home Screen has no Notification global at all.
function permissionDenied(): boolean {
  return typeof Notification !== 'undefined' && Notification.permission === 'denied';
}
