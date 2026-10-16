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
    this.state.set(subscription ? 'on' : 'off');
    if (subscription) {
      // Re-register: the backend may have pruned the row, or never stored it after a failed POST.
      await this.register(subscription).catch(() =>
        this.error.set('Couldn’t confirm this device with the server.'),
      );
    }
  }

  protected async toggle(event: Event): Promise<void> {
    this.busy.set(true);
    this.error.set('');
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
