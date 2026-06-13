# backend

.NET 10 service. Subscribes to `sensors/+/moisture` on the Mosquitto
broker and inserts each reading into the Postgres table `readings`
(`device_id`, `raw`, `percent`, `received_at`). The schema is managed by EF
Core migrations, applied on startup. A REST API serves the data, and in the
container Kestrel also serves the Angular frontend from `wwwroot` (built into
the image from `frontend/` at the repo root).

Endpoints:

| Route | Purpose |
|-------|---------|
| `GET /api/sensors/unassigned` | sensors not yet bound to a plant |
| `GET /api/readings?deviceId=&since=&limit=` | a device's readings, newest first |
| `GET/POST /api/plants`, `GET/PUT/DELETE /api/plants/{id}` | plant CRUD (latest reading joined) |
| `GET /api/species` | plant species list |

A plant binds at most one sensor via a unique `device_id` (one sensor per
plant); assigning a taken sensor returns `409`. `POST`/`PUT /api/plants` take a
`speciesName` that is upserted by name, so a freshly typed species joins the list.

Stack: `Microsoft.NET.Sdk.Web` (MVC controllers), MQTTnet 5, Npgsql + EF Core.
Layered as controllers → services → repositories (LINQ over EF Core).

## Schema / migrations

EF Core owns the schema. `IngestWorker` runs `Database.Migrate()` on startup,
so the database is created and kept up to date automatically. Add or change a
table with:

```sh
dotnet ef migrations add <Name> --project PlantMonitor.Backend
```

Layout: `PlantMonitor.Backend/` (service), `PlantMonitor.Backend.Tests/`
(xunit), tied together by `PlantMonitor.Backend.slnx` — open that in Rider.

Configuration (`appsettings.json`, overridable via env vars as in
`docker-compose.yml`):

| Key | Default | Compose value |
|-----|---------|---------------|
| `Mqtt:Host` / `Mqtt__Host` | `localhost` | `mqtt` |
| `Mqtt:Port` / `Mqtt__Port` | `1883` | — |
| `ConnectionStrings:Db` | local Postgres, no password | `Host=db;...` with password from `.env` |

The Postgres password lives in the repo-root `.env` (gitignored) and is
injected by compose; it is never committed.

Runs as part of the compose stack (`docker compose up -d` at repo root —
the image builds from `Dockerfile` here). The MQTT connection retries every
5 s, so broker restarts are survived.

Local run outside Docker (needs the .NET 10 SDK; broker and Postgres ports
are published by the compose stack). The committed `appsettings.json` has no
DB password — supply the connection string with the password from `.env`,
e.g. as an environment variable (works the same in a Rider run config):

```sh
source ../.env
cd PlantMonitor.Backend
ConnectionStrings__Db="Host=localhost;Username=plantmonitor;Password=$POSTGRES_PASSWORD;Database=plantmonitor" dotnet run
```

## Tests

```sh
dotnet test
```

Unit tests cover payload parsing; the integration tests use
[Testcontainers](https://dotnet.testcontainers.org/) to start throwaway
`postgres:17` and `eclipse-mosquitto:2` containers, run the real
`IngestWorker` against them, publish over MQTT and assert the rows — Docker
must be running, nothing else is needed.
