using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class PushTests
{
    private static Plant Plant(int must = 20, int can = 40) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Monstera",
        DeviceId = "dev-1",
        MustWaterPercent = must,
        CanWaterPercent = can,
    };

    private static (ReadingService Service, IPushService Push) Subject(Plant? plant, int? previousPercent)
    {
        var readings = Substitute.For<IReadingRepository>();
        var plants = Substitute.For<IPlantRepository>();
        var push = Substitute.For<IPushService>();
        var previous = previousPercent is { } p
            ? new ReadingRow { DeviceId = "dev-1", Percent = p, ReceivedAt = DateTimeOffset.UtcNow.AddHours(-1) }
            : null;
        readings.GetLatestForDeviceAsync("dev-1", Arg.Any<CancellationToken>()).Returns(previous);
        plants.GetByDeviceIdAsync("dev-1", Arg.Any<CancellationToken>()).Returns(plant);
        return (new ReadingService(readings, plants, push, NullLogger<ReadingService>.Instance), push);
    }

    private static Task<bool> Record(ReadingService service, int percent) =>
        service.RecordAsync(new Reading("dev-1", percent * 50, percent), default);

    private static Task NoPush(IPushService push) =>
        push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

    [Theory]
    [InlineData(60, 30, WaterStatus.Can)]  // ok -> can
    [InlineData(60, 10, WaterStatus.Must)] // ok -> must, skipping can
    [InlineData(30, 10, WaterStatus.Must)] // can -> must
    public async Task Notifies_once_when_the_state_worsens(int before, int percent, WaterStatus expected)
    {
        var plant = Plant();
        var (service, push) = Subject(plant, before);

        await Record(service, percent);

        await push.Received(1).NotifyAsync(plant, expected, percent, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(10, 30)] // must -> can, recovering
    [InlineData(10, 60)] // must -> ok, watered
    [InlineData(30, 60)] // can -> ok
    [InlineData(10, 10)] // must holds
    public async Task Stays_silent_unless_the_state_worsens(int before, int percent)
    {
        var (service, push) = Subject(Plant(), before);

        await Record(service, percent);

        await NoPush(push);
    }

    [Fact]
    public async Task Stays_silent_on_a_sensor_s_first_reading()
    {
        // 10% is already below must: the plant was dry before the sensor first reported.
        var (service, push) = Subject(Plant(), previousPercent: null);

        await Record(service, 10);

        await NoPush(push);
    }

    [Fact]
    public async Task Stays_silent_for_a_plant_with_no_limits()
    {
        var plant = Plant();
        plant.MustWaterPercent = null;
        plant.CanWaterPercent = null;
        var (service, push) = Subject(plant, 60);

        await Record(service, 10);

        await NoPush(push);
    }

    [Fact]
    public async Task A_failed_push_check_still_reports_the_reading_as_stored()
    {
        var readings = Substitute.For<IReadingRepository>();
        readings.GetLatestForDeviceAsync("dev-1", Arg.Any<CancellationToken>())
            .Returns(new ReadingRow { DeviceId = "dev-1", Percent = 60, ReceivedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        var plants = Substitute.For<IPlantRepository>();
        plants.GetByDeviceIdAsync("dev-1", Arg.Any<CancellationToken>())
            .Returns<Plant?>(_ => throw new InvalidOperationException("db down"));
        var service = new ReadingService(
            readings, plants, Substitute.For<IPushService>(), NullLogger<ReadingService>.Instance);

        Assert.True(await Record(service, 10));
        await readings.Received(1).AddAsync(Arg.Any<ReadingRow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_a_reading_from_an_unassigned_sensor()
    {
        var (service, push) = Subject(plant: null, 60);

        Assert.True(await Record(service, 10));
        await NoPush(push);
    }

    private static PushSubscriptionRow Subscription(string endpoint) =>
        new() { Endpoint = endpoint, P256dh = "key", Auth = "auth" };

    [Fact]
    public async Task Push_service_sends_the_payload_ngsw_expects_to_every_subscriber()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Subscription("https://push.example/a"), Subscription("https://push.example/b")]);
        sender.SendAsync(Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PushSendResult.Delivered);
        var plant = Plant();

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyAsync(plant, WaterStatus.Must, 12, default);

        await sender.Received(2).SendAsync(
            Arg.Any<PushSubscriptionRow>(),
            Arg.Is<string>(p => p.Contains("\"title\":\"Monstera needs water\"")
                && p.Contains("\"body\":\"12% \\u2014 must water below 20%\"")
                && p.Contains($"\"url\":\"/plant/{plant.Id}\"")
                && p.Contains("navigateLastFocusedOrOpen")
                && p.Contains("\"renotify\":true")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_sends_an_offline_notice_pointing_at_the_sensor()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Subscription("https://push.example/a")]);

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyOfflineAsync(Plant(), TimeSpan.FromHours(3.7), default);

        await sender.Received(1).SendAsync(
            Arg.Any<PushSubscriptionRow>(),
            Arg.Is<string>(p => p.Contains("\"title\":\"Monstera sensor offline\"")
                && p.Contains("\"body\":\"No reading for 3 h \\u2014 the device may need a restart\"")
                && p.Contains("\"tag\":\"sensor-dev-1\"")
                && p.Contains("\"url\":\"/sensor/dev-1\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_drops_a_subscription_the_push_service_calls_gone()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Subscription("https://push.example/dead"), Subscription("https://push.example/live")]);
        sender.SendAsync(Arg.Is<PushSubscriptionRow>(s => s.Endpoint.EndsWith("dead")),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Gone);
        sender.SendAsync(Arg.Is<PushSubscriptionRow>(s => s.Endpoint.EndsWith("live")),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(PushSendResult.Delivered);

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyAsync(Plant(), WaterStatus.Can, 30, default);

        await subs.Received(1).DeleteByEndpointAsync("https://push.example/dead", Arg.Any<CancellationToken>());
        await subs.DidNotReceive().DeleteByEndpointAsync("https://push.example/live", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_swallows_a_database_error()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        subs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PushSubscriptionRow>>(_ => throw new InvalidOperationException("db down"));

        await new PushService(subs, Substitute.For<IPushSender>(), NullLogger<PushService>.Instance)
            .NotifyAsync(Plant(), WaterStatus.Must, 12, default);
    }

    [Fact]
    public async Task Push_service_sends_nothing_when_nobody_is_subscribed()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyAsync(Plant(), WaterStatus.Must, 5, default);

        await sender.DidNotReceive().SendAsync(
            Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_test_reaches_only_the_requested_subscription()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Subscription("https://push.example/a"), Subscription("https://push.example/b")]);
        sender.SendAsync(Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PushSendResult.Delivered);

        var result = await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .SendTestAsync("https://push.example/a", default);

        Assert.Equal(PushSendResult.Delivered, result);
        await sender.Received(1).SendAsync(
            Arg.Is<PushSubscriptionRow>(s => s.Endpoint == "https://push.example/a"),
            Arg.Is<string>(p => p.Contains("\"title\":\"Plant Monitor\"") && p.Contains("\"url\":\"/settings\"")),
            Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(
            Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_test_returns_null_for_an_unknown_endpoint()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Subscription("https://push.example/a")]);

        var result = await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .SendTestAsync("https://push.example/other", default);

        Assert.Null(result);
        await sender.DidNotReceive().SendAsync(
            Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_test_prunes_an_expired_subscription_too()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Subscription("https://push.example/dead")]);
        sender.SendAsync(Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PushSendResult.Gone);

        var result = await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .SendTestAsync("https://push.example/dead", default);

        Assert.Equal(PushSendResult.Gone, result);
        await subs.Received().DeleteByEndpointAsync("https://push.example/dead", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_upserts_a_subscription_by_endpoint()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .SubscribeAsync(new PushSubscriptionInput("https://push.example/x", "key", "auth"), default);

        await subs.Received().UpsertAsync(
            Arg.Is<PushSubscriptionRow>(s => s.Endpoint == "https://push.example/x"
                && s.P256dh == "key" && s.Auth == "auth"),
            Arg.Any<CancellationToken>());
    }
}
