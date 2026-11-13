using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Cargo;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Cargo feeds share: creating a feed, and the requests Cargo sends.
/// </summary>
internal static class CargoTestClient
{
    /// <summary>
    /// Creates a Cargo feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateCargoFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Cargo };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    public static string IndexUrl(string feedSlug)
    {
        return $"feeds/{feedSlug}/cargo/index/";
    }

    public static string IndexFileUrl(string feedSlug, string name)
    {
        return IndexUrl(feedSlug) + CargoPackageName.GetIndexPath(name);
    }

    public static string DownloadUrl(string feedSlug, string name, string version)
    {
        return $"feeds/{feedSlug}/cargo/api/v1/crates/{name}/{version}/download";
    }

    /// <summary>
    /// Publishes a crate the way <c>cargo publish</c> does.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="feedSlug">The feed.</param>
    /// <param name="metadata">The metadata; by default <see cref="CargoCrateBuilder.Metadata"/>.</param>
    /// <param name="crate">The crate; by default one built for the name and version of the metadata.</param>
    public static async Task<HttpResponseMessage> PublishCrateAsync(this HttpClient client, string feedSlug, JsonObject metadata, byte[] crate = null)
    {
        crate ??= CargoCrateBuilder.Create(metadata["name"]!.GetValue<string>(), metadata["vers"]!.GetValue<string>());

        var content = new ByteArrayContent(CargoCrateBuilder.PublishBody(metadata, crate));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.PutAsync($"feeds/{feedSlug}/cargo/api/v1/crates/new", content);
    }

    public static Task<HttpResponseMessage> PublishCrateAsync(this HttpClient client, string feedSlug, string name, string version)
    {
        return client.PublishCrateAsync(feedSlug, CargoCrateBuilder.Metadata(name, version));
    }

    /// <summary>
    /// The lines of a crate's index file.
    /// </summary>
    public static async Task<JsonElement[]> GetIndexLinesAsync(this HttpClient client, string feedSlug, string name)
    {
        var text = await client.GetStringAsync(IndexFileUrl(feedSlug, name));

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var entries = new JsonElement[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            using var document = JsonDocument.Parse(lines[i]);
            entries[i] = document.RootElement.Clone();
        }

        return entries;
    }

    /// <summary>
    /// The message Cargo shows for an error response: the detail of its first error.
    /// </summary>
    public static async Task<string> ReadCargoErrorAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors")[0].GetProperty("detail").GetString();
    }

    /// <summary>
    /// Sends a token from now on the way Cargo does: as the whole <c>Authorization</c> header.
    /// </summary>
    public static void UseCargoToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
    }
}
