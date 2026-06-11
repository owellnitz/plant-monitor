# backend

.NET 10 worker service. Subscribes to `sensors/+/moisture` on the Mosquitto
broker and inserts each reading into the Postgres table `readings`
(`device_id`, `raw`, `percent`, `received_at`). The table is created on
startup if missing. No API yet.

Stack: `Microsoft.NET.Sdk.Worker`, MQTTnet 5, Npgsql (raw SQL, no ORM).

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
ConnectionStrings__Db="Host=localhost;Username=plantmonitor;Password=$POSTGRES_PASSWORD;Database=plantmonitor" dotnet run
```
