using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Xunit;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Dart feeds share: creating a feed, and publishing the way <c>dart pub publish</c> does.
/// </summary>
internal static class PubTestClient
{
    public const string PubMediaType = "application/vnd.pub.v2+json";

    /// <summary>
    /// Creates a Dart feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreatePubFeedAsync(this WebApplicationFactory<Startup> factory, string slug, string mirrorSource = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Pub };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// Runs the three steps of publishing a package and returns the response of the last one,
    /// which reports the outcome. The first two steps must succeed.
    /// </summary>
    public static async Task<HttpResponseMessage> PublishAsync(this HttpClient client, string feedSlug, Stream archive)
    {
        // 1. Ask where to upload.
        using var newVersion = await client.GetAsync($"feeds/{feedSlug}/api/packages/versions/new");
        Assert.Equal(HttpStatusCode.OK, newVersion.StatusCode);

        using var upload = JsonDocument.Parse(await newVersion.Content.ReadAsStringAsync());
        var uploadUrl = upload.RootElement.GetProperty("url").GetString();

        // 2. Upload the archive with the fields the server asked for.
        using var form = new MultipartFormDataContent();
        foreach (var field in upload.RootElement.GetProperty("fields").EnumerateObject())
        {
            form.Add(new StringContent(field.Value.GetString()), field.Name);
        }

        var file = new StreamContent(archive);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "package.tar.gz");

        using var uploaded = await client.PostAsync(uploadUrl, form);
        Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);

        // 3. Finalize.
        return await client.GetAsync(uploaded.Headers.Location);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task<string> ReadErrorMessageAsync(this HttpResponseMessage response)
    {
        Assert.Equal(PubMediaType, response.Content.Headers.ContentType?.MediaType);

        return (await response.ReadJsonAsync()).GetProperty("error").GetProperty("message").GetString();
    }
}
