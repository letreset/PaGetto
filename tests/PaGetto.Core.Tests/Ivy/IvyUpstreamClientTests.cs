using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Ivy;
using PaGetto.Core.Maven;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Ivy;

public class IvyUpstreamClientTests
{
    public class ParseFolders
    {
        private static readonly Uri ListingUrl = new("https://repo.example.com/ivy/com.example/sbt-tool/");

        [Fact]
        public void ReadsAnApacheStyleListing()
        {
            var folders = IvyUpstreamClient.ParseFolders(ListingUrl, """
                <html><body><h1>Index of /ivy/com.example/sbt-tool</h1><pre>
                <a href="?C=N;O=D">Name</a>
                <a href="../">../</a>
                <a href="1.0.0/">1.0.0/</a>          01-Jan-2024 12:00    -
                <a href="1.1.0-RC1/">1.1.0-RC1/</a>  01-Feb-2024 12:00    -
                <a href="scala_2.12/">scala_2.12/</a>
                <a href="maven-metadata.xml">maven-metadata.xml</a>
                </pre></body></html>
                """);

            Assert.Equal(new[] { "1.0.0", "1.1.0-RC1", "scala_2.12" }, folders);
        }

        [Fact]
        public void KeepsOnlyDirectChildrenOfTheListing()
        {
            var folders = IvyUpstreamClient.ParseFolders(ListingUrl, """
                <a href='https://repo.example.com/ivy/com.example/sbt-tool/2.0.0/'>2.0.0</a>
                <a class="link" href="/ivy/com.example/sbt-tool/3.0.0/">3.0.0</a>
                <a href="https://repo.example.com/ivy/com.example/">Parent</a>
                <a href="https://other.example.com/ivy/com.example/sbt-tool/4.0.0/">elsewhere</a>
                <a href="5.0.0/jars/">deeper</a>
                <a href="2.0.0/">2.0.0 again</a>
                """);

            Assert.Equal(new[] { "2.0.0", "3.0.0" }, folders);
        }
    }

    public class ListRevisionsAsync : FactsBase
    {
        [Fact]
        public async Task TheFirstMirrorThatListsTheModuleWinsAndMirrorsThatFailAreSkipped()
        {
            AddMirror("https://down.example.com/ivy");
            AddMirror("https://first.example.com/ivy/");
            AddMirror("https://second.example.com/ivy");
            Responses["https://first.example.com/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/"] = """<a href="1.0.0/">1.0.0/</a>""";
            Responses["https://second.example.com/ivy/com.example/sbt-tool/scala_2.12/sbt_1.0/"] = """<a href="2.0.0/">2.0.0/</a>""";

            var revisions = await Target.ListRevisionsAsync(Feed, "com.example/sbt-tool/scala_2.12/sbt_1.0", CancellationToken.None);

            Assert.Equal(new[] { "1.0.0" }, revisions);
        }

        [Fact]
        public async Task LeavesOutTheCrossVersionFoldersOfPlugins()
        {
            AddMirror("https://repo.example.com/ivy");
            Responses["https://repo.example.com/ivy/com.example/sbt-tool/"] = """
                <a href="0.9.0/">0.9.0/</a>
                <a href="scala_2.10/">scala_2.10/</a>
                <a href="sbt_0.13/">sbt_0.13/</a>
                """;

            var revisions = await Target.ListRevisionsAsync(Feed, "com.example/sbt-tool", CancellationToken.None);

            Assert.Equal(new[] { "0.9.0" }, revisions);
        }

        [Fact]
        public async Task IsEmptyWithoutMirrors()
        {
            Assert.Empty(await Target.ListRevisionsAsync(Feed, "com.example/sbt-tool", CancellationToken.None));
        }
    }

    public abstract class FactsBase
    {
        private readonly List<MirrorOptions> _mirrors = [];

        protected FactsBase()
        {
            var feedSettings = new Mock<IFeedSettingsResolver>();
            feedSettings.Setup(s => s.GetMirrorOptions(It.IsAny<Feed>())).Returns(_mirrors);
            feedSettings.Setup(s => s.GetUpstreamListingCacheDuration(It.IsAny<Feed>())).Returns(TimeSpan.Zero);

            var clients = new Mock<IUpstreamClientFactory>();
            clients
                .Setup(c => c.CreateHttpClient(It.IsAny<MirrorOptions>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                .Returns(() => new HttpClient(new StubHandler(Responses)));

            var cache = new UpstreamListingCache();
            var files = new MavenUpstreamClient(feedSettings.Object, clients.Object, cache, NullLogger<MavenUpstreamClient>.Instance);
            Target = new IvyUpstreamClient(feedSettings.Object, clients.Object, cache, files, NullLogger<IvyUpstreamClient>.Instance);
        }

        protected Feed Feed { get; } = new Feed { Id = Guid.NewGuid(), Slug = "ivy", Type = FeedType.Ivy };
        protected Dictionary<string, string> Responses { get; } = [];
        protected IvyUpstreamClient Target { get; }

        protected void AddMirror(string source)
        {
            _mirrors.Add(new MirrorOptions { Enabled = true, PackageSource = new Uri(source) });
        }
    }

    /// <summary>
    /// Answers the URLs it knows with their content, URLs of hosts named "down" with 500, and everything else with 404.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;

        public StubHandler(Dictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var response = _responses.TryGetValue(url, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/html") }
                : new HttpResponseMessage(request.RequestUri.Host.StartsWith("down", StringComparison.Ordinal) ? HttpStatusCode.InternalServerError : HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
