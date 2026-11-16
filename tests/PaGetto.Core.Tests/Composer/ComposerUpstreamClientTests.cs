using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaGetto.Core.Composer;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerUpstreamClientTests
{
    public class ListVersionsAsync : FactsBase
    {
        [Fact]
        public async Task ExpandsMinifiedMetadata()
        {
            // As Packagist writes it: each version only has what differs from the one before it.
            AddMirror("https://repo.example.com");
            Responses["https://repo.example.com/p2/acme/hello.json"] = """
                {"minified": "composer/2.0", "packages": {"acme/hello": [
                    {"name": "acme/hello", "version": "2.0.0", "version_normalized": "2.0.0.0", "license": ["MIT"], "require": {"php": ">=8.1"},
                     "dist": {"type": "zip", "url": "https://code.example.com/acme/hello/2.0.0.zip", "reference": "b", "shasum": ""}},
                    {"version": "1.0.0", "version_normalized": "1.0.0.0", "require": "__unset",
                     "dist": {"type": "zip", "url": "https://code.example.com/acme/hello/1.0.0.zip", "reference": "a", "shasum": ""}},
                    {"version": "not a version"}
                ]}}
                """;

            var versions = await Target.ListVersionsAsync(Feed, "acme/hello", dev: false, CancellationToken.None);

            Assert.Equal(new[] { "2.0.0", "1.0.0" }, versions.Select(v => v.Version));
            Assert.Equal(new[] { "2.0.0.0", "1.0.0.0" }, versions.Select(v => v.NormalizedVersion));

            var first = JsonNode.Parse(versions[1].Json)!;
            Assert.Equal("acme/hello", first["name"]!.GetValue<string>());
            Assert.Equal("MIT", first["license"]![0]!.GetValue<string>());
            Assert.Null(first["require"]);
            Assert.Equal("a", first["dist"]!["reference"]!.GetValue<string>());
        }

        [Fact]
        public async Task ListsOnlyTheVersionsOfTheRequestedFile()
        {
            AddMirror("https://repo.example.com/");
            Responses["https://repo.example.com/p2/acme/hello~dev.json"] = """
                {"packages": {"acme/hello": [{"version": "dev-main"}, {"version": "1.0.0"}, {"version": "2.x-dev"}]}}
                """;

            var versions = await Target.ListVersionsAsync(Feed, "acme/hello", dev: true, CancellationToken.None);

            Assert.Equal(new[] { "dev-main", "2.x-dev" }, versions.Select(v => v.Version));
        }

        [Fact]
        public async Task UsesTheNextMirrorWhenOneDoesNotHaveThePackage()
        {
            AddMirror("https://first.example.com/");
            AddMirror("https://second.example.com/");
            Responses["https://first.example.com/p2/acme/hello.json"] = """{"packages": {"acme/other": [{"version": "1.0.0"}]}}""";
            Responses["https://second.example.com/p2/acme/hello.json"] = """{"packages": {"acme/hello": [{"version": "1.0.0"}]}}""";

            var version = Assert.Single(await Target.ListVersionsAsync(Feed, "acme/hello", dev: false, CancellationToken.None));

            Assert.Equal("https://second.example.com/", version.Mirror.PackageSource.AbsoluteUri);
        }

        [Fact]
        public async Task MirrorThatAnswersWithSomethingElseIsSkipped()
        {
            AddMirror("https://repo.example.com/");
            Responses["https://repo.example.com/p2/acme/hello.json"] = "<html>";

            Assert.Empty(await Target.ListVersionsAsync(Feed, "acme/hello", dev: false, CancellationToken.None));
        }
    }

    public class DownloadDistOrNullAsync : FactsBase
    {
        [Theory]
        [InlineData("https://repo.example.com/dists/acme/hello/1.0.0.zip", true)]
        [InlineData("https://api.github.com/repos/acme/hello/zipball/abc", false)]
        public async Task SendsCredentialsOnlyToTheMirrorsHost(string url, bool authenticate)
        {
            var mirror = AddMirror("https://repo.example.com/");
            Responses[url] = "zip";

            using var zip = await Target.DownloadDistOrNullAsync(Feed, Version(mirror, url, shasum: ""), long.MaxValue, CancellationToken.None);

            Assert.NotNull(zip);
            Assert.Equal(new[] { authenticate }, Authenticated);
            Assert.Equal("PaGetto", Assert.Single(UserAgents));
        }

        [Fact]
        public async Task ChecksTheShasumOfTheMetadata()
        {
            var mirror = AddMirror("https://repo.example.com/");
            Responses["https://repo.example.com/a.zip"] = "zip";
            Responses["https://repo.example.com/b.zip"] = "zip";
            var sha1 = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes("zip")));

            using var right = await Target.DownloadDistOrNullAsync(Feed, Version(mirror, "https://repo.example.com/a.zip", sha1), long.MaxValue, CancellationToken.None);
            using var wrong = await Target.DownloadDistOrNullAsync(Feed, Version(mirror, "https://repo.example.com/b.zip", new string('0', 40)), long.MaxValue, CancellationToken.None);

            Assert.NotNull(right);
            Assert.Null(wrong);
        }

        [Theory]
        [InlineData("""{"type": "tar", "url": "https://repo.example.com/a.tar"}""")]
        [InlineData("""{"type": "zip", "url": "file:///etc/passwd"}""")]
        [InlineData("""{"type": "zip"}""")]
        public async Task OnlyZipsAtHttpUrlsAreDownloaded(string dist)
        {
            var mirror = AddMirror("https://repo.example.com/");
            var version = new ComposerUpstreamVersion { Version = "1.0.0", Json = $"{{\"dist\": {dist}}}", Mirror = mirror };

            Assert.Null(await Target.DownloadDistOrNullAsync(Feed, version, long.MaxValue, CancellationToken.None));
            Assert.Empty(Authenticated);
        }

        private static ComposerUpstreamVersion Version(MirrorOptions mirror, string url, string shasum)
        {
            var json = new JsonObject
            {
                ["version"] = "1.0.0",
                ["dist"] = new JsonObject { ["type"] = "zip", ["url"] = url, ["shasum"] = shasum },
            };

            return new ComposerUpstreamVersion { Version = "1.0.0", NormalizedVersion = "1.0.0.0", Json = json.ToJsonString(), Mirror = mirror };
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
                .Returns((MirrorOptions _, Guid _, bool authenticate) =>
                {
                    Authenticated.Add(authenticate);
                    return new HttpClient(new StubHandler(Responses, UserAgents));
                });

            Target = new ComposerUpstreamClient(feedSettings.Object, clients.Object, new UpstreamListingCache(), NullLogger<ComposerUpstreamClient>.Instance);
        }

        protected Feed Feed { get; } = new Feed { Id = Guid.NewGuid(), Slug = "php", Type = FeedType.Composer };
        protected Dictionary<string, string> Responses { get; } = [];
        protected List<bool> Authenticated { get; } = [];
        protected List<string> UserAgents { get; } = [];
        protected ComposerUpstreamClient Target { get; }

        protected MirrorOptions AddMirror(string source)
        {
            var mirror = new MirrorOptions { Enabled = true, PackageSource = new Uri(source) };
            _mirrors.Add(mirror);
            return mirror;
        }
    }

    /// <summary>
    /// Answers the URLs it knows with their content, and everything else with 404.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;
        private readonly List<string> _userAgents;

        public StubHandler(Dictionary<string, string> responses, List<string> userAgents)
        {
            _responses = responses;
            _userAgents = userAgents;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.UserAgent.Count > 0)
            {
                _userAgents.Add(request.Headers.UserAgent.ToString());
            }

            var url = request.RequestUri!.AbsoluteUri;
            var response = _responses.TryGetValue(url, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(content))) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
