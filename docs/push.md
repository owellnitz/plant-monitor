# Push Notifications

The PWA notifies you when a plant crosses into *can water* or *must water*.
One notification per crossing, on every subscribed browser.

```
reading arrives over MQTT → stored → compared against the plant's limits
   → worse than the state the plant was last seen in?
   → backend POSTs to the browser's push service (Apple, Google, Mozilla)
   → push service delivers, queuing for up to 24 h while the phone is offline
   → the notification shows even if the app was never opened
```

Delivery does **not** depend on being at home. The backend needs outbound
internet (it already has it for firmware releases); the phone gets the message
on any network. Being away when a plant crossed its limit is exactly the case
the push service's queue covers.

## Prerequisites

**HTTPS with a publicly-trusted certificate.** Service workers and the Push API
are unavailable on `http://<lan-ip>`, and iOS Safari refuses Web Push behind a
private CA even when its root is installed. The
[deployment repo](https://github.com/owellnitz/plant-monitor-deployment) covers
this: Caddy terminates TLS with a real Let's Encrypt certificate obtained
through the ACME DNS-01 challenge, so the host stays LAN-only and no port is
forwarded.

`http://localhost:5001` is a secure context too, which is what makes the dev
stack testable in a desktop browser.

## Setup

Generate one VAPID keypair per deployment:

```sh
npx web-push generate-vapid-keys
```

Set three variables in the stack's `.env`:

| Variable | Value |
|----------|-------|
| `WEBPUSH_PUBLIC_KEY` | the generated public key |
| `WEBPUSH_PRIVATE_KEY` | the generated private key |
| `WEBPUSH_SUBJECT` | a contact URL or `mailto:` — push services want one |

They reach the backend as `WebPush__PublicKey` and friends. Leave them unset
and push switches itself off end to end: `GET /api/push/vapid-key` answers 503
and the app hides the notification toggle.

Rotating the keypair invalidates every stored subscription. Existing browsers
keep a subscription the new key cannot sign for, so each one has to toggle
notifications off and on again.

### On the iPhone

1. Open `https://<PLANT_HOST>` in Safari.
2. Share → **Add to Home Screen**.
3. Open the app **from the Home Screen** — Web Push does not work in a Safari
   tab, and the toggle will not appear there.
4. Settings → switch on **Watering notifications**, accept the prompt.

iOS asks once. If you decline, the only way back is to remove the app from the
Home Screen and add it again.

Once the toggle is on, **Send test notification** delivers a fixed message to
every subscribed browser and reports how many took it. Use it: notifications
otherwise only fire on a threshold crossing, which may be days away, so a setup
that never worked looks exactly like one that has had nothing to report. A
count of zero means the push service rejected every stored subscription —
toggle notifications off and on again to re-subscribe.

## When a notification fires

The rule mirrors the traffic light in the UI. Backend and frontend each hold a
copy — [`WaterStatus.cs`](../backend/PlantMonitor.Backend/WaterStatus.cs) and
[`moisture.ts`](../frontend/src/app/moisture.ts) — kept in step by matching
test cases.

Every stored reading updates the plant's `notified_status`. A push goes out
only when the new state is *worse* than the recorded one:

| From | To | Notification |
|------|----|--------------|
| ok | can water | yes |
| ok | must water | yes (once, as *must water*) |
| can water | must water | yes |
| must water | can water | no — recovering |
| anything | ok | no |
| anything | same state | no |
| *nothing recorded yet* | anything | no — this is the baseline |

That last row is what keeps things quiet when you switch notifications on while
plants are already dry: their state is recorded silently, and the next real
crossing is the first thing you hear about. It also covers plants created after
the feature shipped.

Watering a plant back to *ok* re-arms it for the next dry spell. Editing a
plant's limits so it lands in a worse state notifies on the next reading.
Plants with no limits set never notify.

Subscriptions are keyed on the push endpoint, not on a user — the API has no
auth, like the rest of the stack. A subscription the push service reports as
`404`/`410` (app deleted, permission revoked) is removed automatically.

## Known gaps

- **A silent sensor never notifies.** Notifications hang off incoming readings,
  so a sensor that dies mid-summer produces silence rather than a warning while
  the plant dries out. Watch the Sensors page for a stale timestamp.
- **Tapping a notification away from home cannot load the app.** `<PLANT_HOST>`
  only resolves and routes on the LAN. The title and body carry the plant name
  and the actual reading for that reason — the notification is meant to be
  useful without opening anything.
- **Messages expire after 24 h.** A phone offline longer than that misses the
  notification entirely; nothing re-sends, because the crossing already
  happened.

## Testing it locally

Chrome on the machine running the dev stack, via `http://localhost:5001`
(a secure context, so no certificate is needed):

```sh
npx web-push generate-vapid-keys     # into .env as the three variables above
docker compose up -d --build
```

Switch the toggle on in **Settings**, then publish a wet reading followed by a
dry one for a sensor bound to a plant that has limits:

```sh
mosquitto_pub -h localhost -t 'sensors/sensor-001/moisture' \
  -m '{"id":"sensor-001","raw":3500,"percent":80}'
# wait 5 minutes
mosquitto_pub -h localhost -t 'sensors/sensor-001/moisture' \
  -m '{"id":"sensor-001","raw":500,"percent":10}'
```

**The wait matters.** The backend drops a second reading from the same device
inside 5 minutes as a replay, and a dropped reading is never evaluated — two
back-to-back publishes look exactly like a broken feature.

Check what the backend thinks each plant's state is:

```sh
docker compose exec db psql -U plantmonitor -c 'SELECT name, notified_status FROM plants;'
```
