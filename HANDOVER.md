# Handover — HTTPS deployment

Companion to branch `feat/https-caddy` in
[plant-monitor-deployment](https://github.com/owellnitz/plant-monitor-deployment).
That branch puts Caddy in front of the stack so the PWA is served over a real
certificate. Delete this file once the follow-ups below are done.

## TL;DR

**No code change is required in this repo.** The deployment change was checked
against this codebase and everything already works on the new origin. What is
left is two documentation updates and one regression trap worth knowing about.

## What changes on the mini PC

| | Before | After |
|---|---|---|
| Browser / PWA | `http://192.168.0.50` | `https://<PLANT_HOST>` (deSEC hostname, real Let's Encrypt cert) |
| ESP32 OTA | `http://192.168.0.50:80/api/firmware/*` | unchanged — still plain HTTP on the LAN IP |
| MQTT | `192.168.0.50:1883` | unchanged |
| Backend port | published as `:80` | not published; Caddy proxies `backend:8080` internally |

The certificate is issued through the ACME **DNS-01** challenge, so Let's
Encrypt never connects to the mini PC and no port is forwarded on the router.
The host stays LAN-only.

## Why this repo needs no code change (verified)

| Concern | Finding |
|---|---|
| Frontend API base URL | `frontend/src/app/plant-api.ts` uses relative paths (`/api/sensors`, `/api/readings`, …) → same-origin behind Caddy, no mixed content |
| CORS | `backend/PlantMonitor.Backend/Program.cs:21-24` — dev-only, production is same-origin |
| Absolute URL generation | None found in the backend. `FirmwareController` returns metadata only (`version`, `size`, `sha256`); the device builds `/api/firmware/binary?version=…` itself (`firmware/src/ota.rs`), so no `X-Forwarded-Proto` handling is needed |
| PWA manifest | `frontend/public/manifest.webmanifest` — `scope` and `start_url` are `"./"`, so origin-agnostic |
| Service worker | `provideServiceWorker` already registered in `frontend/src/app/app.config.ts`; `ngsw-config.json` matches `/api/**` relatively |
| Dev stack | `docker-compose.yml` (`:5001`) is untouched by any of this |

## Regression trap — do not force HTTPS on the backend

The ESP32-C3 firmware is `no_std` with no TLS stack. `firmware/src/http.rs`
opens a raw socket and writes `GET /api/firmware/latest HTTP/1.0`. It also has
no DNS, so it reaches the backend by IP only — see `firmware/src/config.rs`
(`mqtt_host` + `backend_port`, which `firmware/config.toml` sets to `80`).

Caddy therefore keeps `/api/firmware/*` reachable over **plain HTTP on the LAN
IP** and redirects every other path on that host to the HTTPS origin.

Adding any of these to `Program.cs` will silently kill firmware updates:

- `app.UseHttpsRedirection()`
- `app.UseHsts()`
- `[RequireHttps]` on `FirmwareController`
- a global HTTPS-only auth/transport policy

The symptom is quiet: devices keep publishing readings over MQTT and report
`"ota":"unreachable"` in the payload forever. Nothing errors server-side.

## Follow-ups in this repo

1. **`docs/ota.md`** — record that in the deployed stack OTA is deliberately
   served over plain HTTP while the rest of the origin is HTTPS, and why
   (no TLS on the device). Link the trap above so it is not "fixed" later.
2. **`README.md:27-33`** — the security note describes the dev stack
   (`:1883`, `:5001`, no auth), which is still accurate. Add one line saying
   the deployed stack terminates TLS in front of the backend and points at the
   deployment repo, so the two do not read as contradictory.

Neither blocks the deployment.

## Operational notes

- **The PWA must be reinstalled.** `https://<PLANT_HOST>` is a different origin
  from `http://192.168.0.50`. The installed app, its service worker, its caches
  and any browser storage do not carry over. Remove the old home-screen icon
  and add it again from the HTTPS URL.
- **No device reprovisioning.** `firmware/config.toml` stays as it is;
  `backend_port = "80"` is still correct.
- **Router DNS rebind protection** must whitelist the hostname, otherwise it
  will not resolve on the LAN. See the deployment repo's README, step 4.

## Verifying after the deployment lands

```sh
# PWA over a valid certificate
curl -I https://<PLANT_HOST>

# OTA still reachable unencrypted on the IP (expect 200 or 204, not a redirect)
curl -i "http://192.168.0.50/api/firmware/latest?current=firmware-v0.0.0"

# Anything else on the IP is redirected to HTTPS
curl -I http://192.168.0.50/plants

# The devices' own verdict — watch the `ota` field on the next hourly wake.
# "current" or "installed" is good; "unreachable" means the carve-out broke.
mosquitto_sub -h 192.168.0.50 -t 'sensors/#' -v
```

## Out of scope

Web Push notifications are the reason for the HTTPS work but are **not
designed or planned yet**. HTTPS only removes the blocker: a service worker
and the Push API require a secure context. Nothing in this repo implements
subscriptions, VAPID keys, or sending.
