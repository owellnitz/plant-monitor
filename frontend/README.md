# frontend

Angular PWA showing the stored moisture readings: newest first, filterable
by sensor. Mobile-first — cards on small screens, a table from `sm:` up.
Installable as an app (service worker, enabled in production builds only).

Stack: Angular 22 (standalone components, signals), Tailwind CSS 4 +
daisyUI, Vitest + Testing Library.

Data comes from the backend REST API (`GET /api/sensors`,
`GET /api/readings?deviceId=&limit=`). In production the app is built into
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
