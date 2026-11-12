using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Npm;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of npm feeds share: creating a feed, and publishing the way <c>npm publish</c> does.
/// </summary>
internal static class NpmTestClient
{
    /// <summary>
    /// Creates an npm feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateNpmFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Npm };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The URL of a package document, with a scoped name in one segment as clients send it.
    /// </summary>
    public static string PackageUrl(string feedSlug, string name)
    {
        return $"feeds/{feedSlug}/npm/{NpmPackageName.Escape(name)}";
    }

    /// <summary>
    /// The body <c>npm publish</c> sends: the manifest with the tarball's hashes, the tag, and the
    /// tarball as base64.
    /// </summary>
    public static JsonObject CreatePublishBody(string name, string version, byte[] tarball = null, string tag = "latest", JsonObject manifest = null)
    {
        tarball ??= NpmTarballBuilder.Create(name, version, readme: $"# {name}");

        var versionManifest = (JsonObject)(manifest ?? NpmTarballBuilder.CreateManifest(name, version)).DeepClone();
        versionManifest["_id"] = $"{name}@{version}";
        versionManifest["readme"] = $"# {name}";
        versionManifest["dist"] = new JsonObject
        {
            ["integrity"] = "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball)),
#pragma warning disable CA5350 // The npm client sends the SHA-1 shasum.
            ["shasum"] = Convert.ToHexStringLower(SHA1.HashData(tarball)),
#pragma warning restore CA5350
            ["tarball"] = $"http://localhost/{name}/-/{NpmPackageName.GetTarballFileName(name, version)}",
        };

        return new JsonObject
        {
            ["_id"] = name,
            ["name"] = name,
            ["description"] = "A test package.",
            ["dist-tags"] = new JsonObject { [tag] = version },
            ["versions"] = new JsonObject { [version] = versionManifest },
            ["access"] = null,
            ["_attachments"] = new JsonObject
            {
                [$"{name}-{version}.tgz"] = new JsonObject
                {
                    ["content_type"] = "application/octet-stream",
                    ["data"] = Convert.ToBase64String(tarball),
                    ["length"] = tarball.Length,
                },
            },
        };
    }

    public static Task<HttpResponseMessage> PublishAsync(this HttpClient client, string feedSlug, string name, string version, string tag = "latest")
    {
        return client.PutJsonAsync(PackageUrl(feedSlug, name), CreatePublishBody(name, version, tag: tag));
    }

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, JsonNode body)
    {
        return client.PutAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }

    public static async Task<JsonObject> GetJsonObjectAsync(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
    }

    /// <summary>
    /// The <c>error</c> of an npm error response, which the npm client shows.
    /// </summary>
    public static async Task<string> ReadNpmErrorAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString();
    }
}
