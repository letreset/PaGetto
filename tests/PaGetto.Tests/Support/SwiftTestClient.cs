using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Swift feeds share: creating a feed, source archives, and the requests of Swift
/// Package Manager.
/// </summary>
internal static class SwiftTestClient
{
    /// <summary>
    /// The media type Swift Package Manager accepts JSON answers as.
    /// </summary>
    public const string JsonAccept = "application/vnd.swift.registry.v1+json";

    /// <summary>
    /// Creates a Swift feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateSwiftFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Swift };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The manifest of a library package.
    /// </summary>
    public static string PackageSwift(string name = "LinkedList", string toolsVersion = "5.9")
    {
        return $"// swift-tools-version:{toolsVersion}\nimport PackageDescription\n\nlet package = Package(name: \"{name}\", targets: [.target(name: \"{name}\")])\n";
    }

    /// <summary>
    /// A source archive as <c>swift package-registry publish</c> writes it: the package in one folder.
    /// </summary>
    public static byte[] SourceArchive(string readme = null, IDictionary<string, string> extraFiles = null, string folder = "mona.LinkedList")
    {
        var files = new Dictionary<string, string>
        {
            [$"{folder}/Package.swift"] = PackageSwift(),
            [$"{folder}/Sources/LinkedList/LinkedList.swift"] = "public struct LinkedList {}\n",
        };

        if (readme != null)
        {
            files[$"{folder}/README.md"] = readme;
        }

        foreach (var (path, content) in extraFiles ?? new Dictionary<string, string>())
        {
            files[path.Contains('/') ? path : $"{folder}/{path}"] = content;
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The metadata of a release, as <c>package-metadata.json</c> has it.
    /// </summary>
    public static string Metadata(string description = "A linked list", params string[] repositoryUrls)
    {
        return new JsonObject
        {
            ["description"] = description,
            ["licenseURL"] = "https://example.com/LICENSE",
            ["repositoryURLs"] = new JsonArray(repositoryUrls.Select(u => (JsonNode)u).ToArray()),
            ["author"] = new JsonObject { ["name"] = "Mona" },
        }.ToJsonString();
    }

    /// <summary>
    /// Publishes a release the way <c>swift package-registry publish</c> does: a multipart body
    /// whose parts have no file names.
    /// </summary>
    public static async Task<HttpResponseMessage> PublishAsync(
        this HttpClient client,
        string feedSlug,
        string identifier,
        string version,
        byte[] archive,
        string metadata = null,
        byte[] signature = null,
        string signatureFormat = null)
    {
        using var form = new MultipartFormDataContent();

        var archiveContent = new ByteArrayContent(archive);
        archiveContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(archiveContent, "source-archive");

        if (signature != null)
        {
            form.Add(new ByteArrayContent(signature), "source-archive-signature");
        }

        if (metadata != null)
        {
            var metadataContent = new StringContent(metadata, Encoding.UTF8);
            metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(metadataContent, "metadata");
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, ReleaseUrl(feedSlug, identifier, version)) { Content = form };
        request.Headers.Accept.ParseAdd(JsonAccept);
        if (signatureFormat != null)
        {
            request.Headers.Add("X-Swift-Package-Signature-Format", signatureFormat);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// The URL of a release, or of the release list of a package when the version is null.
    /// </summary>
    public static string ReleaseUrl(string feedSlug, string identifier, string version = null)
    {
        var url = $"feeds/{feedSlug}/swift/{identifier.Replace('.', '/')}";
        return version == null ? url : $"{url}/{version}";
    }

    /// <summary>
    /// Reads the versions of a package's release list, as Swift Package Manager does.
    /// </summary>
    /// <returns>The versions in the order listed, or null if the package is not found.</returns>
    public static async Task<IReadOnlyList<string>> GetReleaseVersionsAsync(this HttpClient client, string feedSlug, string identifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUrl(feedSlug, identifier));
        request.Headers.Accept.ParseAdd(JsonAccept);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var list = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return list["releases"]!.AsObject().Select(r => r.Key).ToList();
    }

    /// <summary>
    /// Reads a release's metadata.
    /// </summary>
    public static async Task<JsonObject> GetReleaseAsync(this HttpClient client, string feedSlug, string identifier, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUrl(feedSlug, identifier, version));
        request.Headers.Accept.ParseAdd(JsonAccept);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }
}
