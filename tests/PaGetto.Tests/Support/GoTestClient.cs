using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Go;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Go feeds share: creating a feed, the requests of the go command, and uploads.
/// </summary>
internal static class GoTestClient
{
    /// <summary>
    /// Creates a Go feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateGoFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Go };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The URL of a file of the proxy: <c>@v/list</c>, <c>@latest</c>, or a version's <c>.info</c>,
    /// <c>.mod</c> or <c>.zip</c>, with the module path and version escaped as the go command does.
    /// </summary>
    public static string ProxyUrl(string feedSlug, string module, string file)
    {
        return $"feeds/{feedSlug}/go/{GoModulePath.Escape(module)}/{file}";
    }

    public static string VersionUrl(string feedSlug, string module, string version, string extension)
    {
        return ProxyUrl(feedSlug, module, $"@v/{GoModulePath.Escape(version)}{extension}");
    }

    /// <summary>
    /// Uploads a module zip, by default one built for the module and version.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadModuleAsync(this HttpClient client, string feedSlug, string module, string version, byte[] zip = null)
    {
        var content = new ByteArrayContent(zip ?? GoModuleZipBuilder.Create(module, version));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        return await client.PutAsync(VersionUrl(feedSlug, module, version, ".zip"), content);
    }

    /// <summary>
    /// Sends Basic credentials from now on, as the go command does from a .netrc file.
    /// </summary>
    public static void UseBasicCredentials(this HttpClient client, string username, string password)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
    }
}
