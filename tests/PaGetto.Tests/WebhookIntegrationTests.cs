using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Webhooks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Package changes sent to the webhook of their feed.
/// </summary>
public class WebhookIntegrationTests : IDisposable
{
    private const string HookUrl = "https://hooks.test/pagetto";

    private readonly HookReceiver _receiver = new();
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public WebhookIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, _receiver);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task APublishIsSentSignedToTheFeedsWebhook()
    {
        await _app.CreateNpmFeedAsync("npm", configure: f =>
        {
            f.WebhookUrl = HookUrl;
            f.WebhookSecret = "s3cret";
        });

        using (await _client.PublishAsync("npm", "my-package", "1.0.0"))
        {
        }

        var hook = await _receiver.NextAsync();

        Assert.Equal(HookUrl, hook.Url);
        Assert.Equal("package_upload_succeeded", hook.Event);
        Assert.Equal(WebhookDeliveryService.Sign("s3cret", Encoding.UTF8.GetBytes(hook.Body)), hook.Signature);

        using var body = JsonDocument.Parse(hook.Body);
        Assert.Equal("package_upload_succeeded", body.RootElement.GetProperty("event").GetString());
        Assert.Equal("npm", body.RootElement.GetProperty("feed").GetString());
        Assert.Equal("Npm", body.RootElement.GetProperty("feedType").GetString());
        Assert.Equal("my-package", body.RootElement.GetProperty("packageId").GetString());
        Assert.Equal("1.0.0", body.RootElement.GetProperty("packageVersion").GetString());
    }

    [Fact]
    public async Task OnlyFeedsWithAWebhookSendOne()
    {
        await _app.CreateNpmFeedAsync("quiet");
        await _app.CreateNpmFeedAsync("loud", configure: f => f.WebhookUrl = HookUrl);

        // Changes are sent in order, so once loud's arrives, quiet's has been handled.
        using (await _client.PublishAsync("quiet", "first", "1.0.0"))
        using (await _client.PublishAsync("loud", "second", "1.0.0"))
        {
        }

        var hook = await _receiver.NextAsync();

        Assert.Contains("\"second\"", hook.Body, StringComparison.Ordinal);
        Assert.Null(hook.Signature);
        Assert.True(_receiver.IsEmpty);
    }

    [Fact]
    public void TheSignatureIsTheHmacOfTheBody()
    {
        // The format GitHub's webhooks use, so receivers can check it the same way.
        Assert.Equal(
            WebhookDeliveryService.Sign("key", "{}"u8.ToArray()),
            "sha256=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData("key"u8, "{}"u8)));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _receiver.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Hook(string Url, string Event, string Signature, string Body);

    /// <summary>
    /// Receives the webhook POSTs; every other request (a mirror's) is a 404.
    /// </summary>
    private sealed class HookReceiver : HttpMessageHandler
    {
        private readonly BlockingCollection<Hook> _hooks = new();

        public bool IsEmpty => _hooks.Count == 0;

        public Task<Hook> NextAsync()
        {
            return Task.Run(() => _hooks.TryTake(out var hook, TimeSpan.FromSeconds(10))
                ? hook
                : throw new TimeoutException("No webhook was sent."));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            _hooks.Add(new Hook(
                request.RequestUri!.AbsoluteUri,
                request.Headers.GetValues(WebhookDeliveryService.EventHeader).Single(),
                request.Headers.TryGetValues(WebhookDeliveryService.SignatureHeader, out var signature) ? signature.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)), cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        protected override void Dispose(bool disposing)
        {
            _hooks.Dispose();
            base.Dispose(disposing);
        }
    }
}
