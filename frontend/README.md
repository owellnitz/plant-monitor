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
sun exposure is a fixed list. The Chart.js chart lives in a shared
`MoistureChart` component used by both detail pages.

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
