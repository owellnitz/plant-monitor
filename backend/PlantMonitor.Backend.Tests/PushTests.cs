using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Repositories;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// The fan-out that sends a push to every subscribed browser, including the
/// "does not send" cases — this is meant to run inside MQTT ingest.
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
}
