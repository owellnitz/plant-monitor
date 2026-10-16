export interface VapidKey {
  publicKey: string;
}

export interface PushTestResult {
  delivered: boolean;
}

/** A browser PushSubscription flattened into what the backend stores. */
export interface PushSubscriptionInput {
  endpoint: string;
  p256dh: string;
  auth: string;
}
