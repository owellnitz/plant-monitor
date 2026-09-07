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

  protected async toggle(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
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
