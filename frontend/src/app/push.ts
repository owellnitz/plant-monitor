/** Response of /api/push/vapid-key: the key the browser subscribes with. */
export interface VapidKey {
  publicKey: string;
}

/** Response of /api/push/test: how many subscriptions took the notification. */
export interface PushTestResult {
  delivered: number;
}

/** A browser PushSubscription flattened into what the backend stores. */
export interface PushSubscriptionInput {
  endpoint: string;
  p256dh: string;
  auth: string;
}
