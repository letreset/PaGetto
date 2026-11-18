using System;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Webhooks;

/// <summary>
/// The package changes waiting to be sent to their feed's webhook, by the
/// <see cref="WebhookDeliveryService"/>. A change never waits for its webhook. Singleton.
/// </summary>
public partial class WebhookQueue
{
    /// <summary>
    /// The most changes that wait; more are dropped, so a webhook that is down can't use up memory.
    /// </summary>
    public const int Capacity = 1000;

    private readonly Channel<WebhookEvent> _channel;
    private readonly ILogger<WebhookQueue> _logger;

    public WebhookQueue(ILogger<WebhookQueue> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channel = Channel.CreateBounded<WebhookEvent>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite },
            dropped => LogDropped(dropped.Event, dropped.FeedSlug, Capacity));
    }

    internal ChannelReader<WebhookEvent> Reader => _channel.Reader;

    public void Enqueue(WebhookEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _channel.Writer.TryWrite(change);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook event {Event} of feed {Feed} dropped: {Capacity} events are already waiting")]
    private partial void LogDropped(string @event, string feed, int capacity);
}
