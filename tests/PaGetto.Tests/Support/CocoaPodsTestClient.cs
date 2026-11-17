using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.CocoaPods;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of CocoaPods feeds share: creating a feed, podspecs, the requests of CocoaPods, and publishing.
/// </summary>
internal static class CocoaPodsTestClient
{
    /// <summary>
    /// Creates a CocoaPods feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateCocoaPodsFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.CocoaPods };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// A podspec in its JSON form, with a git source.
    /// </summary>
    public static string Podspec(string name, string version, Action<JsonObject> configure = null)
    {
        var podspec = new JsonObject
        {
            ["name"] = name,
            ["version"] = version,
            ["summary"] = "A test pod",
            ["homepage"] = "https://example.com/" + name,
            ["license"] = new JsonObject { ["type"] = "MIT" },
            ["authors"] = new JsonObject { ["Contoso"] = "dev@example.com" },
            ["platforms"] = new JsonObject { ["ios"] = "12.0" },
            ["source"] = new JsonObject { ["git"] = $"https://example.com/{name}.git", ["tag"] = version },
            ["source_files"] = "Sources/**/*.swift",
            ["swift_versions"] = new JsonArray("5.0"),
        };

        configure?.Invoke(podspec);
        return podspec.ToJsonString();
    }

    /// <summary>
    /// A zip with one source file, as a source archive.
    /// </summary>
    public static byte[] SourceZip(string content = "public struct Hello {}\n")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Sources/Hello.swift").Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Publishes a podspec, optionally with its source archive.
    /// </summary>
    public static async Task<HttpResponseMessage> PublishAsync(
        this HttpClient client,
        string feedSlug,
        string podspec,
        byte[] source = null,
        string sourceFileName = "source.zip")
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(podspec)), "podspec", "pod.podspec.json" },
        };

        if (source != null)
        {
            form.Add(new ByteArrayContent(source), "source", sourceFileName);
        }

        return await client.PostAsync($"feeds/{feedSlug}/cocoapods/api/v1/pods", form);
    }

    /// <summary>
    /// The URL of a version's podspec, as CocoaPods builds it.
    /// </summary>
    public static string PodspecUrl(string feedSlug, string name, string version)
    {
        return $"feeds/{feedSlug}/cocoapods/{CocoaPodsSpecRepository.GetPodspecPath(name, version)}";
    }

    /// <summary>
    /// The URL of the index file of a pod's shard.
    /// </summary>
    public static string IndexUrl(string feedSlug, string name)
    {
        return $"feeds/{feedSlug}/cocoapods/{CocoaPodsSpecRepository.GetIndexFileName(CocoaPodsPodName.GetShard(name))}";
    }

    /// <summary>
    /// Reads the versions of a pod from the index file of its shard, as CocoaPods does.
    /// </summary>
    /// <returns>The versions, or null if the index does not list the pod.</returns>
    public static async Task<IReadOnlyList<string>> GetPodVersionsAsync(this HttpClient client, string feedSlug, string name)
    {
        var index = await client.GetStringAsync(IndexUrl(feedSlug, name));
        return CocoaPodsSpecRepository.ReadIndex(index).GetValueOrDefault(name);
    }

    /// <summary>
    /// Reads a version's podspec.
    /// </summary>
    public static async Task<JsonObject> GetPodspecAsync(this HttpClient client, string feedSlug, string name, string version)
    {
        return JsonNode.Parse(await client.GetStringAsync(PodspecUrl(feedSlug, name, version)))!.AsObject();
    }
}
