using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Starts real postgres + mosquitto containers once for the class; each
/// test runs the actual IngestWorker against them.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    public PostgreSqlContainer Db { get; } = new PostgreSqlBuilder("postgres:17")
        .Build();

    // The image ships /mosquitto-no-auth.conf for exactly this use case;
    // matches the anonymous access of the real broker.
    public IContainer Mqtt { get; } = new ContainerBuilder("eclipse-mosquitto:2")
        .WithCommand("mosquitto", "-c", "/mosquitto-no-auth.conf")
        .WithPortBinding(1883, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilMessageIsLogged("mosquitto version .+ running"))
        .Build();

    public int MqttPort => Mqtt.GetMappedPublicPort(1883);

    public Task InitializeAsync() => Task.WhenAll(Db.StartAsync(), Mqtt.StartAsync());

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await Mqtt.DisposeAsync();
    }
}

public class IngestIntegrationTests(StackFixture stack) : IClassFixture<StackFixture>
{
    [Fact]
    public async Task Published_reading_is_stored_in_postgres()
    {
        using var host = await StartHostAsync();
        try
        {
            var row = await PublishUntilStoredAsync("it-plant",
                """{"id":"it-plant","raw":3450,"percent":60}""");

            Assert.Equal(3450, row.Raw);
            Assert.Equal(60, row.Percent);

            // PublishUntilStoredAsync republishes the payload until it lands;
            // the dedup window must collapse those replays into one row.
            Assert.Equal(1, await CountReadingsAsync("it-plant"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Malformed_payload_is_skipped_and_worker_keeps_running()
    {
        using var host = await StartHostAsync();
        try
        {
            using var client = await ConnectClientAsync();
            for (var i = 0; i < 5; i++)
                await client.PublishStringAsync("sensors/it-broken/moisture", "this is not json");

            // A later valid reading must still be stored — the malformed
            // ones neither crashed the worker nor produced rows.
            await PublishUntilStoredAsync("it-valid",
                """{"id":"it-valid","raw":2000,"percent":30}""");

            Assert.Equal(0, await CountUnexpectedReadingsAsync("it-plant", "it-valid"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Builds the host and applies migrations before the worker starts, so the
    /// readings table exists before any test query — the worker migrates on its
    /// own schedule, which races the test otherwise.
    /// </summary>
    private async Task<IHost> StartHostAsync()
    {
        var host = BuildIngestHost();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await host.StartAsync();
        return host;
    }

    private IHost BuildIngestHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mqtt:Host"] = stack.Mqtt.Hostname,
            ["Mqtt:Port"] = stack.MqttPort.ToString(),
        });
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(stack.Db.GetConnectionString()));
        builder.Services.AddPlantMonitor();
        builder.Services.AddHostedService<IngestWorker>();
        return builder.Build();
    }

    private async Task<IMqttClient> ConnectClientAsync()
    {
        var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(stack.Mqtt.Hostname, stack.MqttPort)
            .Build());
        return client;
    }

    /// <summary>
    /// QoS 0 to a topic nobody subscribes to yet is silently dropped, and the
    /// worker subscribes asynchronously — republish until the row appears.
    /// </summary>
    private async Task<(int Raw, int Percent)> PublishUntilStoredAsync(string deviceId, string payload)
    {
        using var client = await ConnectClientAsync();
        await using var dataSource = NpgsqlDataSource.Create(stack.Db.GetConnectionString());

        for (var i = 0; i < 60; i++)
        {
            await client.PublishStringAsync($"sensors/{deviceId}/moisture", payload);
            await Task.Delay(500);

            await using var cmd = dataSource.CreateCommand(
                "SELECT raw, percent FROM readings WHERE device_id = $1 LIMIT 1");
            cmd.Parameters.AddWithValue(deviceId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetInt32(0), reader.GetInt32(1));
        }

        throw new TimeoutException($"Reading for {deviceId} was never stored");
    }

    private async Task<long> CountReadingsAsync(string deviceId)
    {
        await using var dataSource = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await using var cmd = dataSource.CreateCommand(
            "SELECT count(*) FROM readings WHERE device_id = $1");
        cmd.Parameters.AddWithValue(deviceId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountUnexpectedReadingsAsync(params string[] expectedDeviceIds)
    {
        await using var dataSource = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await using var cmd = dataSource.CreateCommand(
            "SELECT count(*) FROM readings WHERE device_id != ALL($1)");
        cmd.Parameters.AddWithValue(expectedDeviceIds);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
