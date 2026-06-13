using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Hosts the minimal API against the postgres container; the MQTT worker
/// is not registered, so the schema is created explicitly.
/// </summary>
public class ApiIntegrationTests(StackFixture stack) : IClassFixture<StackFixture>
{
    [Fact]
    public async Task Sensors_returns_each_device_with_its_latest_reading()
    {
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await Schema.EnsureAsync(db, CancellationToken.None);
        await InsertReadingAsync(db, "api-b", 2000, 40);
        await InsertReadingAsync(db, "api-a", 3000, 60);
        await InsertReadingAsync(db, "api-a", 3100, 62);

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            var sensors = await client.GetFromJsonAsync<Sensor[]>("/api/sensors");

            Assert.NotNull(sensors);
            var a = Assert.Single(sensors, s => s.DeviceId == "api-a");
            Assert.Equal(62, a.Percent); // the newest of the two api-a readings
            var b = Assert.Single(sensors, s => s.DeviceId == "api-b");
            Assert.Equal(40, b.Percent);

            var ids = sensors.Select(s => s.DeviceId).ToArray();
            Assert.Equal(ids.Order(), ids);
            Assert.Equal(ids.Distinct().Count(), ids.Length);
        }
    }

    [Fact]
    public async Task Readings_filters_by_since()
    {
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await Schema.EnsureAsync(db, CancellationToken.None);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        await InsertReadingAsync(db, "api-since", 1000, 10, cutoff.AddHours(-1));
        await InsertReadingAsync(db, "api-since", 2000, 20, cutoff.AddMinutes(30));

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            var readings = await client.GetFromJsonAsync<StoredReading[]>(
                $"/api/readings?deviceId=api-since&since={Uri.EscapeDataString(cutoff.ToString("o"))}");

            Assert.NotNull(readings);
            var reading = Assert.Single(readings);
            Assert.Equal(2000, reading.Raw);
        }
    }

    [Fact]
    public async Task Readings_filters_by_device_and_orders_newest_first()
    {
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await Schema.EnsureAsync(db, CancellationToken.None);
        await InsertReadingAsync(db, "api-filter", 1000, 10);
        await InsertReadingAsync(db, "api-filter", 2000, 20);
        await InsertReadingAsync(db, "api-filter", 3000, 30);
        await InsertReadingAsync(db, "api-other", 9000, 90);

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            var readings = await client.GetFromJsonAsync<StoredReading[]>(
                "/api/readings?deviceId=api-filter&limit=2");

            Assert.NotNull(readings);
            Assert.Equal(2, readings.Length);
            Assert.All(readings, r => Assert.Equal("api-filter", r.DeviceId));
            Assert.True(readings[0].ReceivedAt >= readings[1].ReceivedAt);
            Assert.Equal(3000, readings[0].Raw);
        }
    }

    private async Task<(WebApplication App, HttpClient Client)> StartApiAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(NpgsqlDataSource.Create(stack.Db.GetConnectionString()));

        var app = builder.Build();
        app.MapApi();
        await app.StartAsync();

        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static async Task InsertReadingAsync(NpgsqlDataSource db, string deviceId, int raw, int percent,
        DateTimeOffset? receivedAt = null)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO readings (device_id, raw, percent, received_at) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(raw);
        cmd.Parameters.AddWithValue(percent);
        cmd.Parameters.AddWithValue(receivedAt ?? DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }
}
