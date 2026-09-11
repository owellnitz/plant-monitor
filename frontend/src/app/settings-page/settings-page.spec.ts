import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';
import { BehaviorSubject } from 'rxjs';
import { SettingsPage } from './settings-page';

// The component awaits promises between renders; a macrotask lets them settle.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

function subscription(endpoint = 'https://push.example/abc') {
  return {
    endpoint,
    toJSON: () => ({ endpoint, keys: { p256dh: 'p256dh-key', auth: 'auth-secret' } }),
  } as unknown as PushSubscription;
}

function fakeSwPush(isEnabled: boolean, current: PushSubscription | null = null) {
  const subject = new BehaviorSubject<PushSubscription | null>(current);
  return {
    isEnabled,
    subscription: subject,
    requestSubscription: vi.fn(async () => {
      subject.next(subscription());
      return subscription();
    }),
    unsubscribe: vi.fn(async () => subject.next(null)),
  };
}

async function setup(swPush: ReturnType<typeof fakeSwPush>) {
  const view = await render(SettingsPage, {
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: SwPush, useValue: swPush },
    ],
  });
  const settle = async () => {
    await tick();
    await view.fixture.whenStable();
  };
  await settle();
  return { http: TestBed.inject(HttpTestingController), settle };
}

describe('SettingsPage', () => {
  it('explains the missing service worker instead of showing a toggle', async () => {
    const { http } = await setup(fakeSwPush(false));

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByText(/add it to the Home Screen/)).toBeTruthy();
    http.verify(); // the key is never even requested
  });

  it('hides the toggle when the server has no VAPID keys', async () => {
    const { http, settle } = await setup(fakeSwPush(true));
    http
      .expectOne('/api/push/vapid-key')
      .flush('Web Push is not configured.', { status: 503, statusText: 'Service Unavailable' });
    await settle();

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByText(/Push is not configured on this server/)).toBeTruthy();
    http.verify();
  });

  it('subscribes and posts the flattened subscription when switched on', async () => {
    const swPush = fakeSwPush(true);
    const { http, settle } = await setup(swPush);
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    const toggle = screen.getByRole('checkbox');
    expect((toggle as HTMLInputElement).checked).toBe(false);
    await userEvent.click(toggle);

    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: 'BN4-public-key' });
    const posted = http.expectOne('/api/push/subscriptions');
    expect(posted.request.method).toBe('POST');
    expect(posted.request.body).toEqual({
      endpoint: 'https://push.example/abc',
      p256dh: 'p256dh-key',
      auth: 'auth-secret',
    });
    posted.flush(null);
    await settle();

    expect(screen.getByText(/This device is subscribed/)).toBeTruthy();
    http.verify();
  });

  it('starts switched on when this browser already has a subscription', async () => {
    const { http, settle } = await setup(fakeSwPush(true, subscription()));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true);
    http.verify();
  });

  it('reports how many devices took the test notification', async () => {
    const { http, settle } = await setup(fakeSwPush(true, subscription()));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    await userEvent.click(screen.getByRole('button', { name: 'Send test notification' }));

    const test = http.expectOne('/api/push/test');
    expect(test.request.method).toBe('POST');
    test.flush({ delivered: 2 });
    await settle();

    expect(screen.getByText('Sent to 2 devices.')).toBeTruthy();
    http.verify();
  });

  it('warns instead of claiming success when no device took the test', async () => {
    const { http, settle } = await setup(fakeSwPush(true, subscription()));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    await userEvent.click(screen.getByRole('button', { name: 'Send test notification' }));
    http.expectOne('/api/push/test').flush({ delivered: 0 });
    await settle();

    expect(screen.getByText(/No device accepted it/)).toBeTruthy();
    http.verify();
  });

  it('offers no test button while notifications are off', async () => {
    const { http, settle } = await setup(fakeSwPush(true));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    expect(screen.queryByRole('button', { name: 'Send test notification' })).toBeNull();
    http.verify();
  });

  it('unsubscribes the browser and the backend when switched off', async () => {
    const swPush = fakeSwPush(true, subscription());
    const { http, settle } = await setup(swPush);
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    await userEvent.click(screen.getByRole('checkbox'));

    expect(swPush.unsubscribe).toHaveBeenCalled();
    const deleted = http.expectOne(
      (r) => r.method === 'DELETE' && r.url === '/api/push/subscriptions',
    );
    expect(deleted.request.params.get('endpoint')).toBe('https://push.example/abc');
    deleted.flush(null);
    await settle();

    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false);
    http.verify();
  });
});
