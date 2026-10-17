using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class SensorWatchWorkerTests
{
    private static readonly DateTimeOffset LastReading = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly IPlantRepository plants = Substitute.For<IPlantRepository>();
    private readonly IReadingRepository readings = Substitute.For<IReadingRepository>();
    private readonly IPushService push = Substitute.For<IPushService>();
    private readonly SensorWatchWorker worker;

    public SensorWatchWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => plants);
        services.AddScoped(_ => readings);
        services.AddScoped(_ => push);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Push:OfflineHours"] = "3" })
            .Build();
        worker = new SensorWatchWorker(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            config, NullLogger<SensorWatchWorker>.Instance);
    }

    private Plant Assigned(string deviceId)
    {
        var plant = new Plant { Id = Guid.NewGuid(), Name = "Monstera", DeviceId = deviceId };
        plants.GetAllAsync(Arg.Any<CancellationToken>()).Returns([plant]);
        return plant;
    }

    private void LatestReadingAt(DateTimeOffset receivedAt) =>
        readings.GetLatestForDevicesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ReadingRow { DeviceId = "dev-1", ReceivedAt = receivedAt }]);

    private Task CheckAfter(double hours) => worker.CheckAsync(LastReading.AddHours(hours), default);

    private Task Offline(int times) =>
        push.Received(times).NotifyOfflineAsync(Arg.Any<Plant>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Stays_silent_while_the_sensor_reports_within_the_threshold()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(2.9);

        await Offline(0);
    }

    [Fact]
    public async Task Notifies_once_per_outage()
    {
        var plant = Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(3);
        await CheckAfter(4);
        await CheckAfter(10);

        await push.Received(1).NotifyOfflineAsync(plant, TimeSpan.FromHours(3), Arg.Any<CancellationToken>());
        await Offline(1);
    }

    [Fact]
    public async Task A_fresh_reading_re_arms_the_sensor()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);
        await CheckAfter(3);

        LatestReadingAt(LastReading.AddHours(4));
        await CheckAfter(4.5);
        await CheckAfter(7);

        await Offline(2);
    }

    [Fact]
    public async Task Watches_only_sensors_assigned_to_a_plant()
    {
        plants.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([new Plant { Id = Guid.NewGuid(), Name = "No sensor" }, new Plant { Id = Guid.NewGuid(), Name = "Monstera", DeviceId = "dev-1" }]);
        readings.GetLatestForDevicesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        await CheckAfter(10);

        await readings.Received(1).GetLatestForDevicesAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "dev-1" })), Arg.Any<CancellationToken>());
        await Offline(0);
    }
}
