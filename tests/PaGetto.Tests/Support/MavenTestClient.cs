using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Xunit;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Maven feeds share: creating a feed, and deploying the way <c>mvn deploy</c> does.
/// </summary>
internal static class MavenTestClient
{
    /// <summary>
    /// Creates a Maven feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateMavenFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Maven };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// The URL of a path in the feed's repository.
    /// </summary>
    public static string Url(string feedSlug, string path)
    {
        return $"feeds/{feedSlug}/maven2/{path}";
    }

    /// <summary>
    /// The URL of a file of a version.
    /// </summary>
    public static string FileUrl(string feedSlug, string groupId, string artifactId, string version, string fileName)
    {
        return Url(feedSlug, $"{groupId.Replace('.', '/')}/{artifactId}/{version}/{fileName}");
    }

    public static string Pom(string groupId, string artifactId, string version, string packaging = "jar", string description = "A test library.")
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>{groupId}</groupId>
              <artifactId>{artifactId}</artifactId>
              <version>{version}</version>
              <packaging>{packaging}</packaging>
              <description>{description}</description>
              <dependencies>
                <dependency><groupId>org.slf4j</groupId><artifactId>slf4j-api</artifactId><version>2.0.9</version></dependency>
              </dependencies>
            </project>
            """;
    }

    /// <summary>
    /// A JAR's worth of bytes; the server doesn't look inside.
    /// </summary>
    public static byte[] Jar(string marker)
    {
        return Encoding.UTF8.GetBytes($"PK\u0003\u0004 {marker}");
    }

    /// <summary>
    /// Deploys a release version as <c>mvn deploy</c> does: each file followed by its SHA-1 and MD5,
    /// then the artifact's metadata. Asserts that every upload succeeds.
    /// </summary>
    /// <returns>The JAR that was deployed.</returns>
    public static async Task<byte[]> DeployAsync(this HttpClient client, string feedSlug, string groupId, string artifactId, string version, string description = "A test library.")
    {
        var jar = Jar($"{artifactId} {version}");

        await client.PutFileAsync(FileUrl(feedSlug, groupId, artifactId, version, $"{artifactId}-{version}.jar"), jar);
        await client.PutFileAsync(FileUrl(feedSlug, groupId, artifactId, version, $"{artifactId}-{version}.pom"), Encoding.UTF8.GetBytes(Pom(groupId, artifactId, version, description: description)));
        await client.PutFileAsync(Url(feedSlug, $"{groupId.Replace('.', '/')}/{artifactId}/maven-metadata.xml"), Encoding.UTF8.GetBytes("<metadata/>"));

        return jar;
    }

    /// <summary>
    /// Uploads a file and its SHA-1 and MD5 checksums, and asserts that all three succeed.
    /// </summary>
    public static async Task PutFileAsync(this HttpClient client, string url, byte[] content)
    {
        using (var response = await client.PutAsync(url, new ByteArrayContent(content)))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"PUT {url}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

#pragma warning disable CA5350, CA5351 // The checksums Maven uploads.
        foreach (var (extension, checksum) in new[] { (".sha1", SHA1.HashData(content)), (".md5", MD5.HashData(content)) })
#pragma warning restore CA5350, CA5351
        {
            using var response = await client.PutAsync(url + extension, new StringContent(Convert.ToHexStringLower(checksum)));
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"PUT {url}{extension}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    public static async Task<HttpResponseMessage> PutBytesAsync(this HttpClient client, string url, byte[] content)
    {
        return await client.PutAsync(url, new ByteArrayContent(content));
    }

    public static IReadOnlyDictionary<string, string> Checksums(byte[] content)
    {
#pragma warning disable CA5350, CA5351 // The checksums Maven downloads.
        return new Dictionary<string, string>
        {
            ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(content)),
            ["md5"] = Convert.ToHexStringLower(MD5.HashData(content)),
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            ["sha512"] = Convert.ToHexStringLower(SHA512.HashData(content)),
        };
#pragma warning restore CA5350, CA5351
    }
}
