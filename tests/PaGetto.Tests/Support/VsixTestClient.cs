using System;
using System.Collections.Generic;
using System.IO;
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
using PaGetto.Core.Vsix;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of VSIX feeds share: creating a feed, the requests of Visual Studio, and uploads.
/// </summary>
internal static class VsixTestClient
{
    /// <summary>
    /// Creates a VSIX feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateVsixFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Vsix };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// Reads the feed's gallery, as Visual Studio does, optionally through the URL with a token.
    /// </summary>
    /// <returns>The entries, by identifier.</returns>
    public static async Task<Dictionary<string, VsixGalleryEntry>> GetVsixGalleryAsync(this HttpClient client, string feedSlug, string token = null)
    {
        var url = new Uri(client.BaseAddress!, token == null ? $"feeds/{feedSlug}/vsix/atom.xml" : $"feeds/{feedSlug}/vsix/auth/{token}/atom.xml");
        var feed = await client.GetByteArrayAsync(url);

        return VsixAtomFeed.Read(new MemoryStream(feed), url).ToDictionary(e => e.Manifest.Id);
    }

    /// <summary>
    /// Uploads a VSIX file as <c>curl -T</c> does, by default one built for the identifier and version.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadVsixAsync(this HttpClient client, string feedSlug, string id, string version, byte[] vsix = null)
    {
        var content = new ByteArrayContent(vsix ?? VsixPackageBuilder.Create(id, version));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.PutAsync($"feeds/{feedSlug}/vsix/upload", content);
    }
}
