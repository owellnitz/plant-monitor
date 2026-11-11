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
    private SensorWatchWorker worker;

    public SensorWatchWorkerTests()
    {
        worker = WithOfflineHours("3");
        NotifyLeavesOwed(NoOne);
    }

    private static readonly string[] NoOne = [];

    /// <summary>Each value is the endpoints one send leaves owed: empty when all took it, null when it threw.</summary>
    private void NotifyLeavesOwed(params string[]?[] owed) =>
        push.NotifyOfflineAsync(default!, default, default, default)
            .ReturnsForAnyArgs(owed[0], owed[1..]);

    private SensorWatchWorker WithOfflineHours(string? hours)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => plants);
        services.AddScoped(_ => readings);
        services.AddScoped(_ => push);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Push:OfflineHours"] = hours })
            .Build();
        return new SensorWatchWorker(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
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

    private Task CheckAfter(double hours, DateTimeOffset startedAt = default) =>
        worker.CheckAsync(LastReading.AddHours(hours), startedAt, default);

    private Task Offline(int times) =>
        push.Received(times).NotifyOfflineAsync(Arg.Any<Plant>(), Arg.Any<TimeSpan>(),
            Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Stays_silent_while_the_sensor_reports_within_the_threshold()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(2.9);

        await Offline(0);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("0", 3)]
    [InlineData("-1", 3)]
    [InlineData("0.5", 3)] // sensors report hourly, so this would alert on healthy ones
    [InlineData(null, 3)]
    [InlineData("", 3)] // what compose passes for an unset variable
    [InlineData("1,5", 3)]
    [InlineData("3h", 3)]
    [InlineData("1e300", 3)]
    [InlineData("Infinity", 3)]
    public async Task Reads_the_threshold_in_fractional_hours_and_falls_back_to_3(string? configured, double hours)
    {
        worker = WithOfflineHours(configured);
        Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(hours - 0.1);
        await Offline(0);

        await CheckAfter(hours);
        await Offline(1);
    }

    [Fact]
    public async Task Notifies_once_per_outage()
    {
        var plant = Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(3);
        await CheckAfter(4);
        await CheckAfter(10);

        await push.Received(1).NotifyOfflineAsync(plant, TimeSpan.FromHours(3), null, Arg.Any<CancellationToken>());
        await Offline(1);
    }

    [Fact]
    public async Task Retries_everyone_when_the_send_threw()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);
        NotifyLeavesOwed(null, NoOne);

        await CheckAfter(3);
        await CheckAfter(3.25);
        await CheckAfter(3.5);

        await push.Received(2).NotifyOfflineAsync(
            Arg.Any<Plant>(), Arg.Any<TimeSpan>(), null, Arg.Any<CancellationToken>());
        await Offline(2);
    }

    [Fact]
    public async Task Retries_only_the_endpoints_whose_send_failed()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);
        NotifyLeavesOwed(["https://push.example/phone"], NoOne);

        await CheckAfter(3);
        await CheckAfter(3.25);
        await CheckAfter(3.5);

        await push.Received(1).NotifyOfflineAsync(Arg.Any<Plant>(), Arg.Any<TimeSpan>(),
            Arg.Is<IReadOnlyCollection<string>?>(only => only != null && only.Single() == "https://push.example/phone"),
            Arg.Any<CancellationToken>());
        await Offline(2);
    }

    [Fact]
    public async Task Silence_while_the_backend_was_down_does_not_count()
    {
        var plant = Assigned("dev-1");
        LatestReadingAt(LastReading);
        var startedAt = LastReading.AddHours(5);

        await CheckAfter(5, startedAt);
        await CheckAfter(7.9, startedAt);
        await Offline(0);

        await CheckAfter(8, startedAt);
        await push.Received(1).NotifyOfflineAsync(plant, TimeSpan.FromHours(8), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_while_push_is_not_configured()
    {
        push.PublicKey.Returns((string?)null);
        Assigned("dev-1");
        LatestReadingAt(LastReading);

        await CheckAfter(10);

        await plants.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
        await Offline(0);
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
    public async Task A_silent_sensor_moved_to_another_plant_is_reported_for_that_plant()
    {
        Assigned("dev-1");
        LatestReadingAt(LastReading);
        await CheckAfter(3);

        var moved = Assigned("dev-1");
        await CheckAfter(3.25);

        await push.Received(1).NotifyOfflineAsync(
            moved, Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>());
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
