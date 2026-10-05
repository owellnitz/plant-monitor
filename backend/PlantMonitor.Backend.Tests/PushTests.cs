using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Dtos;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// The transition rules that make a push fire once per worsening crossing, and
/// the fan-out that sends it. Everything here runs inside MQTT ingest, so the
/// "does not throw / does not send" cases matter as much as the sending ones.
/// </summary>
public class PushTests
{
    private static Plant Plant(WaterStatus? notified, int must = 20, int can = 40) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Monstera",
        DeviceId = "dev-1",
        MustWaterPercent = must,
        CanWaterPercent = can,
        NotifiedStatus = notified,
    };

    private static (ReadingService Service, IPlantRepository Plants, IPushService Push) Subject(Plant? plant)
    {
        var readings = Substitute.For<IReadingRepository>();
        var plants = Substitute.For<IPlantRepository>();
        var push = Substitute.For<IPushService>();
        plants.GetByDeviceIdAsync("dev-1", Arg.Any<CancellationToken>()).Returns(plant);
        return (new ReadingService(readings, plants, push), plants, push);
    }

    private static Task<bool> Record(ReadingService service, int percent) =>
        service.RecordAsync(new Reading("dev-1", percent * 50, percent), default);

    [Theory]
    [InlineData(WaterStatus.Ok, 30, WaterStatus.Can)]   // ok -> can
    [InlineData(WaterStatus.Ok, 10, WaterStatus.Must)]  // ok -> must, skipping can
    [InlineData(WaterStatus.Can, 10, WaterStatus.Must)] // can -> must
    public async Task Notifies_once_when_the_state_worsens(WaterStatus before, int percent, WaterStatus expected)
    {
        var plant = Plant(before);
        var (service, plants, push) = Subject(plant);

        await Record(service, percent);

        await push.Received(1).NotifyAsync(plant, expected, percent, Arg.Any<CancellationToken>());
        Assert.Equal(expected, plant.NotifiedStatus);
        await plants.Received().UpdateAsync(plant, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(WaterStatus.Must, 30)] // must -> can, recovering
    [InlineData(WaterStatus.Must, 60)] // must -> ok, watered
    [InlineData(WaterStatus.Can, 60)]  // can -> ok
    public async Task Records_but_stays_silent_when_the_state_improves(WaterStatus before, int percent)
    {
        var plant = Plant(before);
        var (service, plants, push) = Subject(plant);

        await Record(service, percent);

        await push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await plants.Received().UpdateAsync(plant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Baselines_a_first_seen_plant_without_notifying()
    {
        // The already-dry case: notifications are switched on while the plant is
        // below its limit, so there is no recorded state to have worsened from.
        var plant = Plant(notified: null);
        var (service, _, push) = Subject(plant);

        await Record(service, 10);

        Assert.Equal(WaterStatus.Must, plant.NotifiedStatus);
        await push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_notify_again_while_the_state_holds()
    {
        var plant = Plant(WaterStatus.Must);
        var (service, plants, push) = Subject(plant);

        await Record(service, 10);

        await push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await plants.DidNotReceive().UpdateAsync(Arg.Any<Plant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clears_the_status_of_a_plant_with_no_limits()
    {
        var plant = Plant(WaterStatus.Must);
        plant.MustWaterPercent = null;
        plant.CanWaterPercent = null;
        var (service, _, push) = Subject(plant);

        await Record(service, 10);

        Assert.Null(plant.NotifiedStatus);
        await push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_a_reading_from_an_unassigned_sensor()
    {
        var (service, _, push) = Subject(plant: null);

        Assert.True(await Record(service, 10));
        await push.DidNotReceive().NotifyAsync(
            Arg.Any<Plant>(), Arg.Any<WaterStatus>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
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
        var plant = Plant(WaterStatus.Can);

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyAsync(plant, WaterStatus.Must, 12, default);

        await sender.Received(2).SendAsync(
            Arg.Any<PushSubscriptionRow>(),
            Arg.Is<string>(p => p.Contains("\"title\":\"Monstera needs water\"")
                && p.Contains("\"body\":\"12% \\u2014 must water below 20%\"")
                && p.Contains($"\"url\":\"/plant/{plant.Id}\"")
                && p.Contains("navigateLastFocusedOrOpen")),
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
            .NotifyAsync(Plant(WaterStatus.Ok), WaterStatus.Can, 30, default);

        await subs.Received(1).DeleteByEndpointAsync("https://push.example/dead", Arg.Any<CancellationToken>());
        await subs.DidNotReceive().DeleteByEndpointAsync("https://push.example/live", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Push_service_sends_nothing_when_nobody_is_subscribed()
    {
        var subs = Substitute.For<IPushSubscriptionRepository>();
        var sender = Substitute.For<IPushSender>();
        subs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await new PushService(subs, sender, NullLogger<PushService>.Instance)
            .NotifyAsync(Plant(WaterStatus.Ok), WaterStatus.Must, 5, default);

        await sender.DidNotReceive().SendAsync(
            Arg.Any<PushSubscriptionRow>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
