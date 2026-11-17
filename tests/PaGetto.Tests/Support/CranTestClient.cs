using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of CRAN feeds share: creating a feed, the requests of R, and uploads.
/// </summary>
internal static class CranTestClient
{
    /// <summary>
    /// Creates a CRAN feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateCranFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Cran };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// Reads the feed's <c>PACKAGES.gz</c>, as <c>available.packages</c> does.
    /// </summary>
    /// <returns>The entries, by package name.</returns>
    public static async Task<Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>> GetCranIndexAsync(this HttpClient client, string feedSlug)
    {
        var compressed = await client.GetByteArrayAsync($"feeds/{feedSlug}/cran/src/contrib/PACKAGES.gz");

        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return CranDcf.ReadRecords(reader).ToDictionary(r => CranDcf.GetValue(r, "Package"), r => r);
    }

    /// <summary>
    /// Uploads a source package as <c>curl -T {file} {repository}/src/contrib/</c> does, by default one built for the name and version.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadCranPackageAsync(this HttpClient client, string feedSlug, string name, string version, byte[] package = null)
    {
        return await client.UploadCranFileAsync(feedSlug, CranFileName.Create(name, version), package ?? CranPackageBuilder.Create(name, version));
    }

    /// <summary>
    /// Uploads a file to <c>src/contrib/{fileName}</c>.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadCranFileAsync(this HttpClient client, string feedSlug, string fileName, byte[] package)
    {
        var content = new ByteArrayContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.PutAsync($"feeds/{feedSlug}/cran/src/contrib/{fileName}", content);
    }
}
