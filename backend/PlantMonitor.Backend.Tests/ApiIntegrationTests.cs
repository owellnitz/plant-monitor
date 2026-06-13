using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PlantMonitor.Backend.Controllers;
using PlantMonitor.Backend.Dtos;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Hosts the controllers against the postgres container; the MQTT worker
/// is not registered, so the schema is migrated explicitly.
/// </summary>
public class ApiIntegrationTests(StackFixture stack) : IClassFixture<StackFixture>
{
    [Fact]
    public async Task Readings_filters_by_since()
    {
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await MigrateAsync(stack.Db.GetConnectionString());
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
        await MigrateAsync(stack.Db.GetConnectionString());
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

    [Fact]
    public async Task Plant_crud_and_species_upsert()
    {
        await MigrateAsync(stack.Db.GetConnectionString());
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await InsertReadingAsync(db, "plant-dev", 3000, 60);

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            // Create with a brand-new species name (upserted).
            var create = await client.PostAsJsonAsync("/api/plants",
                new PlantInput("Kitchen basil", "Genovese basil", "Kitchen", "Full sun", "plant-dev"));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<PlantDto>();
            Assert.NotNull(created);
            Assert.Equal("Genovese basil", created!.Species);
            Assert.Equal(60, created.Percent); // latest reading joined

            // The new species now appears in the list.
            var species = await client.GetFromJsonAsync<SpeciesDto[]>("/api/species");
            Assert.Contains(species!, s => s.Name == "Genovese basil");

            // It appears in the plants list.
            var plants = await client.GetFromJsonAsync<PlantDto[]>("/api/plants");
            Assert.Contains(plants!, p => p.Id == created.Id);

            // Update reuses the same species (no duplicate created).
            var update = await client.PutAsJsonAsync($"/api/plants/{created.Id}",
                new PlantInput("Window basil", "Genovese basil", "Window", "Partial sun", "plant-dev"));
            update.EnsureSuccessStatusCode();
            var updated = await update.Content.ReadFromJsonAsync<PlantDto>();
            Assert.Equal("Window basil", updated!.Name);
            var afterSpecies = await client.GetFromJsonAsync<SpeciesDto[]>("/api/species");
            Assert.Equal(1, afterSpecies!.Count(s => s.Name == "Genovese basil"));

            // Delete.
            var del = await client.DeleteAsync($"/api/plants/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
            var after = await client.GetAsync($"/api/plants/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        }
    }

    [Fact]
    public async Task Unassigned_excludes_bound_sensors()
    {
        await MigrateAsync(stack.Db.GetConnectionString());
        await using var db = NpgsqlDataSource.Create(stack.Db.GetConnectionString());
        await InsertReadingAsync(db, "free-b", 2000, 40);
        await InsertReadingAsync(db, "free-a", 1000, 20);
        await InsertReadingAsync(db, "free-a", 1100, 25); // newer reading for free-a
        await InsertReadingAsync(db, "bound-sensor", 2000, 40);

        var (app, client) = await StartApiAsync();
        await using (app)
        {
            await client.PostAsJsonAsync("/api/plants",
                new PlantInput("Bound", null, null, null, "bound-sensor"));

            var unassigned = await client.GetFromJsonAsync<Sensor[]>("/api/sensors/unassigned");
            Assert.NotNull(unassigned);
            Assert.DoesNotContain(unassigned!, s => s.DeviceId == "bound-sensor");

            // One row per device, newest reading, ordered by device id.
            var a = Assert.Single(unassigned!, s => s.DeviceId == "free-a");
            Assert.Equal(25, a.Percent); // the newer of free-a's two readings
            Assert.Contains(unassigned!, s => s.DeviceId == "free-b");

            var ids = unassigned!.Where(s => s.DeviceId.StartsWith("free-")).Select(s => s.DeviceId).ToArray();
            Assert.Equal(ids.Order(), ids);
            Assert.Equal(ids.Distinct().Count(), ids.Length);
        }
    }

    [Fact]
    public async Task Assigning_a_taken_sensor_conflicts()
    {
        await MigrateAsync(stack.Db.GetConnectionString());
        var (app, client) = await StartApiAsync();
        await using (app)
        {
            await client.PostAsJsonAsync("/api/plants", new PlantInput("First", null, null, null, "dup-sensor"));
            var second = await client.PostAsJsonAsync("/api/plants",
                new PlantInput("Second", null, null, null, "dup-sensor"));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
    }

    private async Task<(WebApplication App, HttpClient Client)> StartApiAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(stack.Db.GetConnectionString()));
        builder.Services.AddPlantMonitor();
        builder.Services.AddControllers().AddApplicationPart(typeof(SensorsController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static async Task MigrateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var ctx = new AppDbContext(options);
        await ctx.Database.MigrateAsync();
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
