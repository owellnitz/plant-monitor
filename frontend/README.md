# frontend

Angular PWA for managing plants and the sensors bound to them. Mobile-first;
installable as an app (service worker, enabled in production builds only).

Routes (lazy-loaded):

| Path | Page |
|------|------|
| `/` | plant overview — one card per plant with its latest moisture value |
| `/plant/new` | create a plant (`?deviceId=` prefills a sensor) |
| `/plant/:id` | plant detail — species/location/sun, 7-day Chart.js chart, recent readings, edit/delete |
| `/plant/:id/edit` | edit a plant |
| `/unassigned` | sensors reporting but not yet assigned to a plant |
| `/sensor/:deviceId` | readings chart for an unassigned sensor |

The plant form's species select grows from a free-text "add new species" field;
sun exposure is a fixed list. Two optional limits — must-water % and can-water %
— drive a per-plant watering traffic light: the moisture gauge and status badge
turn red below must-water, amber below can-water, and green above (neutral when no
limits are set). The Chart.js chart lives in a shared `MoistureChart` component
used by both detail pages.

Data loading uses a single pattern — `rxResource` (`@angular/core/rxjs-interop`)
on every page. A `RefreshService` exposes a `version()` signal that each
resource threads into its `params`, so bumping it reloads the current page.

**Offline (PWA).** The service worker prefetches the app shell, so the installed
PWA always opens even when the backend is unreachable. API responses are cached
with a *freshness* strategy (`ngsw-config.json`): online it serves fresh data
(3 s network timeout, then cache), offline it serves the last response for up to
**a week** — outdated, but the app stays usable. (The service worker only runs in
production builds, not `ng serve`.)

**Pull to refresh.** In standalone (installed) mode, where the browser's native
gesture is gone, a `PullToRefresh` component (in the app shell) refreshes the
current page's data when you drag down past the top.

Stack: Angular 22 (standalone components, signals, router), Chart.js,
Tailwind CSS 4 + daisyUI, Vitest + Testing Library.

Data comes from the backend REST API (`/api/plants`, `/api/species`,
`/api/sensors/unassigned`, `/api/readings`). In production the app is built into
the backend image and served by Kestrel from `wwwroot` — `docker compose
up -d` at the repo root is all it takes, then open
[http://localhost:5001](http://localhost:5001).

## Development

The dev server proxies `/api` to the backend on `:5001`
(`proxy.conf.json`), so start the compose stack first:

```sh
docker compose up -d        # at the repo root: broker, Postgres, backend
npm ci
npx ng serve --proxy-config proxy.conf.json   # http://localhost:4200
```

The backend allows CORS from `:4200` in Development, so running it via
`dotnet run` instead of compose works too (see
[backend/README.md](../backend/README.md)).

## Tests & build

```sh
npm test         # Vitest (jsdom)
npm run build    # production build into dist/
```

Formatting is Prettier (`.prettierrc`):

```sh
npx prettier --check src
```
