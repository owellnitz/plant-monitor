using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlantMonitor.Backend.Services;
using Xunit;

namespace PlantMonitor.Backend.Tests;

public class WebPushSenderTests
{
    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static (string Public, string Private) KeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        return (Base64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), Base64Url(p.D!));
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static WebPushSender Sender(HttpStatusCode status, string publicKey, string privateKey)
    {
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StubHandler(status)));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = publicKey,
            ["WebPush:PrivateKey"] = privateKey,
        }).Build();
        return new WebPushSender(http, config, NullLogger<WebPushSender>.Instance);
    }

    private static PushSubscriptionRow Subscription() => new()
    {
        Endpoint = "https://push.example/sub",
        P256dh = KeyPair().Public,
        Auth = Base64Url(RandomNumberGenerator.GetBytes(16)),
    };

    [Theory]
    [InlineData(HttpStatusCode.Created, PushSendResult.Delivered)]
    [InlineData(HttpStatusCode.NotFound, PushSendResult.Gone)]
    [InlineData(HttpStatusCode.Gone, PushSendResult.Gone)]
    [InlineData(HttpStatusCode.InternalServerError, PushSendResult.Failed)]
    public async Task Maps_the_push_service_response(HttpStatusCode status, PushSendResult expected)
    {
        var (publicKey, privateKey) = KeyPair();

        var result = await Sender(status, publicKey, privateKey).SendAsync(Subscription(), "{}", default);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Malformed_keys_turn_push_off_instead_of_throwing()
    {
        var sender = Sender(HttpStatusCode.Created, "typo", "typo");

        Assert.Null(sender.PublicKey);
        Assert.Equal(PushSendResult.Failed, await sender.SendAsync(Subscription(), "{}", default));
    }
}
