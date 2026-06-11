using Npgsql;

namespace PlantMonitor.Backend;

/// <summary>
/// Stores sensor readings in Postgres.
/// </summary>
public sealed class IngestWorker(
    NpgsqlDataSource db,
    ILogger<IngestWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        log.LogInformation("Readings schema ready");
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            """
            CREATE TABLE IF NOT EXISTS readings (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                device_id text NOT NULL,
                raw integer NOT NULL,
                percent integer NOT NULL,
                received_at timestamptz NOT NULL DEFAULT now()
            )
            """);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
