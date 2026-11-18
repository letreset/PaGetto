using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Feeds;

namespace PaGetto.Core.Webhooks;

/// <summary>
/// Sends the changes of the <see cref="WebhookQueue"/> to their feed's webhook, one at a time and
/// in order: a JSON POST, signed with the feed's webhook secret, tried three times.
/// </summary>
public partial class WebhookDeliveryService : BackgroundService
{
    public const string EventHeader = "X-PaGetto-Event";
    public const string DeliveryHeader = "X-PaGetto-Delivery";
    public const string SignatureHeader = "X-PaGetto-Signature";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Pooled connections and no cookies, like the upstream requests.
    private static readonly SocketsHttpHandler _sharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false,
    };

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly WebhookQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpMessageHandler _handler;
    private readonly ILogger<WebhookDeliveryService> _logger;

    /// <param name="queue">The changes to send.</param>
    /// <param name="scopeFactory">Creates the scope that finds a change's feed.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="handlerOverride">The handler requests are sent with; tests send them to a fake.</param>
    public WebhookDeliveryService(
        WebhookQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookDeliveryService> logger,
        HttpMessageHandler handlerOverride = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handler = handlerOverride ?? _sharedHandler;
    }

    /// <summary>
    /// The signature of a body as <see cref="SignatureHeader"/> carries it: <c>sha256=</c> and the
    /// HMAC-SHA256 of the body with the secret, in lowercase hex.
    /// </summary>
    public static string Sign(string secret, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(body);

        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var change in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverAsync(change, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogDeliveryFailed(e, change.Event, change.FeedSlug);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; changes still waiting are not sent.
        }
    }

    private async Task DeliverAsync(WebhookEvent change, CancellationToken cancellationToken)
    {
        string url;
        string secret;
        string feedType;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var feed = await scope.ServiceProvider.GetRequiredService<IFeedService>().GetFeedBySlugAsync(change.FeedSlug, cancellationToken);
            if (string.IsNullOrEmpty(feed?.WebhookUrl))
            {
                return;
            }

            url = feed.WebhookUrl;
            secret = feed.WebhookSecret;
            feedType = feed.Type.ToString();
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                @event = change.Event,
                feed = change.FeedSlug,
                feedType,
                packageId = change.PackageId,
                packageVersion = change.PackageVersion,
                actor = change.Actor,
                timestamp = change.TimestampUtc,
            },
            _json);
        var delivery = Guid.NewGuid().ToString();

        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout };
        for (var attempt = 0; ; attempt++)
        {
            int? status = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                request.Headers.UserAgent.ParseAdd("PaGetto-Webhook");
                request.Headers.Add(EventHeader, change.Event);
                request.Headers.Add(DeliveryHeader, delivery);
                if (!string.IsNullOrEmpty(secret))
                {
                    request.Headers.Add(SignatureHeader, Sign(secret, body));
                }

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    LogDelivered(change.Event, change.FeedSlug, url);
                    return;
                }

                status = (int)response.StatusCode;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                LogAttemptFailed(e, change.Event, change.FeedSlug, url, attempt + 1);
            }

            if (status != null)
            {
                LogAttemptRejected(change.Event, change.FeedSlug, url, attempt + 1, status.Value);
            }

            if (attempt >= RetryDelays.Length)
            {
                LogGaveUp(change.Event, change.FeedSlug, url, attempt + 1);
                return;
            }

            await Task.Delay(RetryDelays[attempt], cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {Event} of feed {Feed} sent to {Url}")]
    private partial void LogDelivered(string @event, string feed, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Event} of feed {Feed}: {Url} answered {StatusCode} (attempt {Attempt})")]
    private partial void LogAttemptRejected(string @event, string feed, string url, int attempt, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Event} of feed {Feed}: {Url} could not be reached (attempt {Attempt})")]
    private partial void LogAttemptFailed(Exception exception, string @event, string feed, string url, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook {Event} of feed {Feed} was not delivered to {Url} after {Attempts} attempts")]
    private partial void LogGaveUp(string @event, string feed, string url, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook {Event} of feed {Feed} could not be sent")]
    private partial void LogDeliveryFailed(Exception exception, string @event, string feed);
}
