import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { PlantApi } from '../plant-api';

/**
 * checking    - reading the browser's and the backend's state
 * unsupported - no service worker (dev build, or a non-HTTPS origin)
 * unavailable - the deployment has no VAPID keys configured
 * denied      - the browser refused; iOS only asks again after a reinstall
 */
type PushState = 'checking' | 'unsupported' | 'unavailable' | 'denied' | 'off' | 'on';

@Component({
  selector: 'app-settings-page',
  imports: [RouterLink],
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
    if (!this.swPush.isEnabled) {
      this.state.set('unsupported');
      return;
    }

    try {
      this.publicKey = (await firstValueFrom(this.api.getVapidKey())).publicKey;
    } catch {
      this.state.set('unavailable');
      return;
    }

    const subscription = await firstValueFrom(this.swPush.subscription);
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
      const { delivered } = await firstValueFrom(this.api.sendTestPush());
      this.testResult.set(
        delivered === 0
          ? 'No device accepted it — try switching notifications off and on again.'
          : `Sent to ${delivered} device${delivered === 1 ? '' : 's'}.`,
      );
    } catch {
      this.error.set('Couldn’t send the test notification.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.testResult.set('');
    try {
      await (this.state() === 'on' ? this.disable() : this.enable());
    } catch {
      // iOS gives no second prompt once denied — say so rather than retry.
      if (Notification.permission === 'denied') {
        this.state.set('denied');
      } else {
        this.error.set('Couldn’t change the notification setting.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async enable(): Promise<void> {
    // Must run straight off the click: iOS only shows the permission prompt
    // for a user gesture.
    const subscription = await this.swPush.requestSubscription({ serverPublicKey: this.publicKey });
    const { endpoint, keys } = subscription.toJSON();

    await firstValueFrom(
      this.api.subscribePush({
        endpoint: endpoint ?? '',
        p256dh: keys?.['p256dh'] ?? '',
        auth: keys?.['auth'] ?? '',
      }),
    );
    this.state.set('on');
  }

  private async disable(): Promise<void> {
    // Read the endpoint before unsubscribing — afterwards it is gone.
    const subscription = await firstValueFrom(this.swPush.subscription);
    await this.swPush.unsubscribe();

    if (subscription) {
      // A 404 means the backend already forgot it, which is the desired end state.
      await firstValueFrom(this.api.unsubscribePush(subscription.endpoint)).catch(() => undefined);
    }
    this.state.set('off');
  }
}
