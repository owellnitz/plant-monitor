using Npgsql;
using NpgsqlTypes;

namespace PlantMonitor.Backend;

/// <summary>
/// A reading as stored in Postgres, served to the frontend.
/// </summary>
public sealed record StoredReading(Guid Id, string DeviceId, int Raw, int Percent, DateTimeOffset ReceivedAt);

public static class Api
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sensors", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var cmd = db.CreateCommand(
                "SELECT DISTINCT device_id FROM readings ORDER BY device_id");
            var sensors = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                sensors.Add(reader.GetString(0));
            return sensors;
        });

        app.MapGet("/api/readings", async (NpgsqlDataSource db, string? deviceId,
            int limit = 50, CancellationToken ct = default) =>
        {
            limit = Math.Clamp(limit, 1, 500);
            await using var cmd = db.CreateCommand(
                """
                SELECT id, device_id, raw, percent, received_at
                FROM readings
                WHERE $1::text IS NULL OR device_id = $1
                ORDER BY received_at DESC
                LIMIT $2
                """);
            // DBNull with a positional parameter needs an explicit type OID.
            cmd.Parameters.Add(new NpgsqlParameter
            { Value = (object?)deviceId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
            cmd.Parameters.AddWithValue(limit);

            var readings = new List<StoredReading>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                readings.Add(new StoredReading(reader.GetGuid(0), reader.GetString(1),
                    reader.GetInt32(2), reader.GetInt32(3), reader.GetFieldValue<DateTimeOffset>(4)));
            return readings;
        });
    }
}
