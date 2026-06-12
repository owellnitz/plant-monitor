# frontend

Angular PWA in two views: a sensor select page (one card per sensor with
its latest moisture value) and a per-sensor detail page with the latest
reading, a Chart.js line chart of the last 7 days, and the most recent
readings. Mobile-first; installable as an app (service worker, enabled in
production builds only).

Stack: Angular 22 (standalone components, signals, router), Chart.js,
Tailwind CSS 4 + daisyUI, Vitest + Testing Library.

Data comes from the backend REST API (`GET /api/sensors`,
`GET /api/readings?deviceId=&since=&limit=`). In production the app is built into
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
