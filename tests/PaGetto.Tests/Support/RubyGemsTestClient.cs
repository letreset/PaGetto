using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of RubyGems feeds share: creating a feed, the requests of Bundler and gem, and pushes.
/// </summary>
internal static class RubyGemsTestClient
{
    /// <summary>
    /// Creates a RubyGems feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateRubyGemsFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.RubyGems };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The gem source of a feed, which Bundler and gem add paths to.
    /// </summary>
    public static string SourceUrl(string feedSlug)
    {
        return $"feeds/{feedSlug}/rubygems/";
    }

    /// <summary>
    /// Pushes a gem as <c>gem push</c> does, by default one built for the name and version.
    /// </summary>
    public static async Task<HttpResponseMessage> PushGemAsync(this HttpClient client, string feedSlug, string name, string version, string platform = null)
    {
        return await client.PushGemAsync(feedSlug, RubyGemBuilder.Create(name, version, platform));
    }

    public static async Task<HttpResponseMessage> PushGemAsync(this HttpClient client, string feedSlug, byte[] gem, string apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SourceUrl(feedSlug) + "api/v1/gems")
        {
            Content = new ByteArrayContent(gem),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // gem push sends the API key as the whole header, without a scheme.
        if (apiKey != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Yanks a version as <c>gem yank</c> does.
    /// </summary>
    public static async Task<HttpResponseMessage> YankGemAsync(this HttpClient client, string feedSlug, string name, string version, string platform = null, string apiKey = null)
    {
        var form = new Dictionary<string, string> { ["gem_name"] = name, ["version"] = version };
        if (platform != null)
        {
            form["platform"] = platform;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, SourceUrl(feedSlug) + "api/v1/gems/yank")
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (apiKey != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// The lines of an index file after its header.
    /// </summary>
    public static async Task<string[]> GetIndexLinesAsync(this HttpClient client, string url)
    {
        var text = await client.GetStringAsync(url);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.SkipWhile(l => l != "---").Skip(1).ToArray();
    }
}
