using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Composer feeds share: creating a feed, the requests of Composer, and uploads.
/// </summary>
internal static class ComposerTestClient
{
    /// <summary>
    /// Creates a Composer feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateComposerFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Composer };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The URL of a package's metadata, which Composer builds from the metadata URL of <c>packages.json</c>.
    /// </summary>
    public static string MetadataUrl(string feedSlug, string name, bool dev = false)
    {
        return $"feeds/{feedSlug}/composer/p2/{name}{(dev ? "~dev" : "")}.json";
    }

    /// <summary>
    /// Reads a package's metadata as Composer does.
    /// </summary>
    /// <returns>The versions of the package.</returns>
    public static async Task<JsonArray> GetVersionsAsync(this HttpClient client, string feedSlug, string name, bool dev = false)
    {
        var metadata = JsonNode.Parse(await client.GetStringAsync(MetadataUrl(feedSlug, name, dev)))!;
        return metadata["packages"]![name]!.AsArray();
    }

    /// <summary>
    /// The version of a package's metadata.
    /// </summary>
    public static JsonObject FindVersion(this JsonArray versions, string version)
    {
        return versions.Select(v => v!.AsObject()).Single(v => v["version"]!.GetValue<string>() == version);
    }

    /// <summary>
    /// Uploads a package zip, by default one built for the name.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadPackageAsync(this HttpClient client, string feedSlug, string name, string version, byte[] zip = null)
    {
        var content = new ByteArrayContent(zip ?? ComposerZipBuilder.Create(name));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        return await client.PutAsync($"feeds/{feedSlug}/composer/packages/upload/{name}/{version}", content);
    }
}
