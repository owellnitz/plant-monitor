using System.Buffers;
using System.Text;
using MQTTnet;
using Npgsql;

namespace PlantMonitor.Backend;

/// <summary>
/// Subscribes to sensors/+/moisture and writes each reading to Postgres.
/// </summary>
public sealed class IngestWorker(
    NpgsqlDataSource db,
    IConfiguration config,
    ILogger<IngestWorker> log) : BackgroundService
{
    private const string Topic = "sensors/+/moisture";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);

        var host = config["Mqtt:Host"] ?? "localhost";
        var port = config.GetValue("Mqtt:Port", 1883);

        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += HandleMessageAsync;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .Build();

        // Connect with retry; also reconnects if the broker drops us.
        while (!ct.IsCancellationRequested)
        {
            if (!client.IsConnected)
            {
                try
                {
                    await client.ConnectAsync(options, ct);
                    await client.SubscribeAsync(
                        new MqttClientSubscribeOptionsBuilder().WithTopicFilter(Topic).Build(), ct);
                    log.LogInformation("Subscribed to {Topic} on {Host}:{Port}", Topic, host, port);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("MQTT connection to {Host}:{Port} failed: {Message}", host, port, ex.Message);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        try
        {
            var json = Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray());
            var reading = Reading.TryParse(json);
            if (reading is null)
            {
                log.LogWarning("Ignoring malformed payload on {Topic}: {Json}", topic, json);
                return;
            }

            await using var cmd = db.CreateCommand(
                "INSERT INTO readings (device_id, raw, percent) VALUES ($1, $2, $3)");
            cmd.Parameters.AddWithValue(reading.Id);
            cmd.Parameters.AddWithValue(reading.Raw);
            cmd.Parameters.AddWithValue(reading.Percent);
            await cmd.ExecuteNonQueryAsync();

            log.LogInformation("Stored reading {DeviceId} raw={Raw} percent={Percent}",
                reading.Id, reading.Raw, reading.Percent);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to store reading from {Topic}", topic);
        }
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
