using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Ivy;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Ivy feeds share: creating a feed, and publishing the way <c>sbt publish</c> does
/// with <c>Resolver.ivyStylePatterns</c>.
/// </summary>
internal static class IvyTestClient
{
    public const string Organisation = "com.example";

    /// <summary>
    /// Creates an Ivy feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateIvyFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Ivy };
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
        return $"feeds/{feedSlug}/ivy/{path}";
    }

    /// <summary>
    /// The URL of a file of a revision of an sbt plugin built for sbt 1.x.
    /// </summary>
    public static string PluginFileUrl(string feedSlug, string module, string revision, string fileName)
    {
        return Url(feedSlug, IvyPath.GetFilePath(Organisation, module, "2.12", "1.0", revision, fileName));
    }

    /// <summary>
    /// The ivy.xml sbt writes for an sbt plugin built for sbt 1.x.
    /// </summary>
    public static string PluginDescriptor(string module, string revision, string description = "A test plugin.")
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ivy-module version="2.0" xmlns:e="http://ant.apache.org/ivy/extra">
                <info organisation="{Organisation}" module="{module}" revision="{revision}" status="release" publication="20200101000000" e:sbtVersion="1.0" e:scalaVersion="2.12">
                    <license name="Apache-2.0" url="https://www.apache.org/licenses/LICENSE-2.0.txt" />
                    <description homepage="https://example.com/{module}">{description}</description>
                </info>
                <configurations>
                    <conf name="compile" visibility="public" description="" />
                </configurations>
                <publications>
                    <artifact name="{module}" type="jar" ext="jar" conf="compile" />
                </publications>
                <dependencies>
                    <dependency org="org.scala-lang" name="scala-library" rev="2.12.20" conf="provided->default(compile)" />
                </dependencies>
            </ivy-module>
            """;
    }

    /// <summary>
    /// Publishes a revision of an sbt plugin as <c>sbt publish</c> does: the JAR and the ivy.xml last,
    /// each followed by its SHA-1 and MD5. Asserts that every upload succeeds.
    /// </summary>
    /// <returns>The JAR that was published.</returns>
    public static async Task<byte[]> PublishPluginAsync(this HttpClient client, string feedSlug, string module, string revision, string description = "A test plugin.")
    {
        var jar = MavenTestClient.Jar($"{module} {revision}");

        await client.PutFileAsync(PluginFileUrl(feedSlug, module, revision, $"jars/{module}.jar"), jar);
        await client.PutFileAsync(
            PluginFileUrl(feedSlug, module, revision, IvyPath.DescriptorFileName),
            Encoding.UTF8.GetBytes(PluginDescriptor(module, revision, description)));

        return jar;
    }
}
