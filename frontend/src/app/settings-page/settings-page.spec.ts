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

function expectResync(http: HttpTestingController) {
  const resync = http.expectOne('/api/push/subscriptions');
  expect(resync.request.method).toBe('POST');
  resync.flush(null);
}

describe('SettingsPage', () => {
  beforeEach(() => vi.stubGlobal('PushManager', class {}));
  afterEach(() => vi.unstubAllGlobals());

  it('explains the missing service worker instead of showing a toggle', async () => {
    const { http } = await setup(fakeSwPush(false));

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByText(/add it to the Home Screen/)).toBeTruthy();
    http.verify(); // the key is never even requested
  });

  it('sends iOS Safari outside the Home Screen to the Home Screen hint', async () => {
    vi.unstubAllGlobals(); // a service worker, but no Push API
    const { http } = await setup(fakeSwPush(true));

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByText(/add it to the Home Screen/)).toBeTruthy();
    http.verify();
  });

  it('explains a permission denied earlier instead of offering a toggle', async () => {
    vi.stubGlobal('Notification', { permission: 'denied' });
    const { http, settle } = await setup(fakeSwPush(true));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByText(/Notifications are blocked/)).toBeTruthy();
    http.verify();
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

  it('starts switched on and re-registers an existing subscription with the server', async () => {
    const { http, settle } = await setup(fakeSwPush(true, subscription()));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();
    expectResync(http);
    await settle();

    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true);
    http.verify();
  });

  it('puts the toggle back and reports an error when the server rejects the subscription', async () => {
    const { http, settle } = await setup(fakeSwPush(true));
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();

    await userEvent.click(screen.getByRole('checkbox'));
    http
      .expectOne('/api/push/subscriptions')
      .flush('invalid', { status: 400, statusText: 'Bad Request' });
    await settle();

    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false);
    expect(screen.getByText(/Couldn’t change the notification setting/)).toBeTruthy();
    http.verify();
  });

  it('reports a refused permission prompt as denied', async () => {
    const swPush = fakeSwPush(true);
    swPush.requestSubscription.mockRejectedValueOnce(new Error('NotAllowedError'));
    const { http, settle } = await setup(swPush);
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();
    vi.stubGlobal('Notification', { permission: 'denied' });

    await userEvent.click(screen.getByRole('checkbox'));
    await settle();

    expect(screen.getByText(/Notifications are blocked/)).toBeTruthy();
    http.verify();
  });

  it('unsubscribes the browser and the backend when switched off', async () => {
    const swPush = fakeSwPush(true, subscription());
    const { http, settle } = await setup(swPush);
    http.expectOne('/api/push/vapid-key').flush({ publicKey: 'BN4-public-key' });
    await settle();
    expectResync(http);
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
