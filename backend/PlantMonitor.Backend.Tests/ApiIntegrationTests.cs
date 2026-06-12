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
    public async Task Sensors_returns_distinct_device_ids()
    {
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await Schema.EnsureAsync(db, CancellationToken.None);
        await InsertReadingAsync(db, "api-b", 2000, 40);
        await InsertReadingAsync(db, "api-a", 3000, 60);
        await InsertReadingAsync(db, "api-a", 3100, 62);

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            var sensors = await client.GetFromJsonAsync<string[]>("/api/sensors");

            Assert.NotNull(sensors);
            Assert.Contains("api-a", sensors);
            Assert.Contains("api-b", sensors);
            Assert.Equal(sensors.Order(), sensors);
            Assert.Equal(sensors.Distinct().Count(), sensors.Length);
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

    private static async Task InsertReadingAsync(NpgsqlDataSource db, string deviceId, int raw, int percent)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO readings (device_id, raw, percent) VALUES ($1, $2, $3)");
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(raw);
        cmd.Parameters.AddWithValue(percent);
        await cmd.ExecuteNonQueryAsync();
    }
}
