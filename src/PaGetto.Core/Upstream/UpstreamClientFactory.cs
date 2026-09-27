using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream.Clients;
using PaGetto.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Upstream;

public partial class UpstreamClientFactory : IUpstreamClientFactory
{
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly DisabledUpstreamClient _disabled;
    private readonly UpstreamListingCache _listingCache;
    private readonly ILoggerFactory _loggerFactory;
    // Allows tests to route upstream requests to an in-memory server. This is deliberately a handler
    // rather than an HttpClient: the app registers a shared HttpClient singleton, and injecting that
    // one would skip the per-feed authentication headers and timeout applied in CreateHttpClient.
    private readonly HttpMessageHandler _handlerOverride;

    public UpstreamClientFactory(
        IFeedSettingsResolver feedSettings,
        DisabledUpstreamClient disabled,
        UpstreamListingCache listingCache,
        ILoggerFactory loggerFactory,
        HttpMessageHandler handlerOverride = null)
    {
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _disabled = disabled ?? throw new ArgumentNullException(nameof(disabled));
        _listingCache = listingCache ?? throw new ArgumentNullException(nameof(listingCache));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _handlerOverride = handlerOverride;
    }

    public IUpstreamClient CreateForFeed(Feed feed)
    {
        var mirrors = _feedSettings.GetMirrorOptions(feed);

        if (mirrors.Count == 0)
            return _disabled;

        var upstream = CreateForMirrors(mirrors, feed.Id);

        // The combined listing of all mirrors is cached, so one lookup serves a whole request.
        var cacheDuration = _feedSettings.GetUpstreamListingCacheDuration(feed);
        if (cacheDuration <= TimeSpan.Zero)
            return upstream;

        return new CachingUpstreamClient(upstream, _listingCache, feed, cacheDuration);
    }

    private IUpstreamClient CreateForMirrors(IReadOnlyList<MirrorOptions> mirrors, Guid feedId)
    {
        // A single upstream is used directly, exactly as before multiple mirrors existed.
        if (mirrors.Count == 1)
            return CreateForMirror(mirrors[0], feedId);

        var upstreams = mirrors.Select(m => CreateForMirror(m, feedId)).ToList();
        return new FallbackUpstreamClient(upstreams, _loggerFactory.CreateLogger<FallbackUpstreamClient>());
    }

    private IUpstreamClient CreateForMirror(MirrorOptions mirrorOptions, Guid feedId)
    {
        if (mirrorOptions.Legacy)
        {
            var snapshot = new StaticOptionsSnapshot<MirrorOptions>(mirrorOptions);
            return new V2UpstreamClient(snapshot, _loggerFactory.CreateLogger<V2UpstreamClient>());
        }

        var logger = _loggerFactory.CreateLogger<UpstreamClientFactory>();
        var httpClient = CreateHttpClient(mirrorOptions, feedId, logger);
        var clientFactory = new NuGetClientFactory(httpClient, mirrorOptions.PackageSource.ToString());
        var nugetClient = new NuGetClient(clientFactory);
        return new V3UpstreamClient(nugetClient, _loggerFactory.CreateLogger<V3UpstreamClient>());
    }

    // Headers that must never be forwarded to upstream feeds regardless of what is stored in the DB.
    // Mirrors the blocklist enforced on the write path in FeedSettings.cshtml.cs.
    private static readonly HashSet<string> _blockedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Host", "Content-Length", "Transfer-Encoding",
        "Connection", "Upgrade", "Proxy-Authorization", "Set-Cookie"
    };

    private HttpClient CreateHttpClient(MirrorOptions options, Guid feedId, ILogger logger)
    {
        var client = _handlerOverride != null
            ? new HttpClient(_handlerOverride, disposeHandler: false)
            : new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

        if (options.PackageDownloadTimeoutSeconds > 0)
            client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

        if (options.Authentication is { } auth)
        {
            switch (auth.Type)
            {
                case MirrorAuthenticationType.Basic:
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                    break;

                case MirrorAuthenticationType.Bearer:
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
                    break;

                case MirrorAuthenticationType.Custom:
                    if (auth.CustomHeaders != null)
                    {
                        foreach (var (header, value) in auth.CustomHeaders)
                        {
                            if (_blockedHeaderNames.Contains(header))
                            {
                                LogBlockedHeaderSkipped(logger, header, feedId, options.PackageSource);
                                continue;
                            }
                            client.DefaultRequestHeaders.Add(header, value);
                        }
                    }
                    break;
            }
        }

        return client;
    }

    private sealed class StaticOptionsSnapshot<T> : IOptionsSnapshot<T> where T : class
    {
        public StaticOptionsSnapshot(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public T Get(string name) => Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping blocked header '{HeaderName}' from the custom headers of feed {FeedId} mirror {PackageSource}.")]
    private static partial void LogBlockedHeaderSkipped(ILogger logger, string headerName, Guid feedId, Uri packageSource);
}
