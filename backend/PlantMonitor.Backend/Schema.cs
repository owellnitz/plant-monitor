using Npgsql;

namespace PlantMonitor.Backend;

/// <summary>
/// Creates the database schema if it doesn't exist yet.
/// </summary>
public static class Schema
{
    public static async Task EnsureAsync(NpgsqlDataSource db, CancellationToken ct)
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
