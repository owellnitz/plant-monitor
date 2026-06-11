using System.Buffers;
using System.Text;
using MQTTnet;
using Npgsql;

namespace PlantMonitor.Backend;

/// <summary>
/// Subscribes to sensors/+/moisture and stores readings in Postgres.
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

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var json = Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray());
        log.LogInformation("Received on {Topic}: {Json}", e.ApplicationMessage.Topic, json);
        return Task.CompletedTask;
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
