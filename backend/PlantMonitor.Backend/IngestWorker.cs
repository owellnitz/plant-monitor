using System.Buffers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MQTTnet;

namespace PlantMonitor.Backend;

/// <summary>
/// Subscribes to sensors/+/moisture and writes each reading to Postgres.
/// </summary>
public sealed class IngestWorker(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration config,
    ILogger<IngestWorker> log) : BackgroundService
{
    private const string Topic = "sensors/+/moisture";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            await db.Database.MigrateAsync(ct);

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

            await using var db = await dbFactory.CreateDbContextAsync();
            db.Readings.Add(new ReadingRow
            {
                DeviceId = reading.Id,
                Raw = reading.Raw,
                Percent = reading.Percent,
            });
            await db.SaveChangesAsync();

            log.LogInformation("Stored reading {DeviceId} raw={Raw} percent={Percent}",
                reading.Id, reading.Raw, reading.Percent);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to store reading from {Topic}", topic);
        }
    }
}
