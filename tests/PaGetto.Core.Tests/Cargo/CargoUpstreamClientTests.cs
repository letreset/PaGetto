using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaGetto.Core.Cargo;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoUpstreamClientTests
{
    private const string Cksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public class ListVersionsAsync : FactsBase
    {
        [Fact]
        public async Task ListsTheVersionsOfTheIndexFileWithTheirDownloadUrls()
        {
            AddMirror("https://index.example.com/");
            Responses["https://index.example.com/config.json"] = "{\"dl\":\"https://static.example.com/crates/{crate}/{crate}-{version}.crate?p={lowerprefix}\"}";
            Responses["https://index.example.com/my/_c/my_crate"] =
                $"{{\"name\":\"My_Crate\",\"vers\":\"1.0.0\",\"deps\":[],\"cksum\":\"{Cksum}\",\"features\":{{}},\"yanked\":true}}\n"
                + $"{{\"name\":\"other\",\"vers\":\"1.1.0\",\"deps\":[],\"cksum\":\"{Cksum}\",\"features\":{{}},\"yanked\":false}}\n"
                + "{\"name\":\"My_Crate\",\"vers\":\"1.2.0\",\"deps\":[],\"cksum\":\"short\",\"features\":{},\"yanked\":false}\n";

            var version = Assert.Single(await Target.ListVersionsAsync(Feed, "my_crate", CancellationToken.None));

            Assert.Equal("My_Crate", version.Name);
            Assert.Equal("1.0.0", version.Version);
            Assert.Equal(Cksum, version.Cksum);
            Assert.True(version.Yanked);
            Assert.Equal("https://static.example.com/crates/My_Crate/My_Crate-1.0.0.crate?p=my/_c", version.DownloadUrl);
            Assert.Contains("\"vers\":\"1.0.0\"", version.IndexLineJson);
        }

        [Fact]
        public async Task DownloadsFromDlWithoutMarkersAtTheDefaultPath()
        {
            AddMirror("https://index.example.com");
            Responses["https://index.example.com/config.json"] = "{\"dl\":\"https://index.example.com/api/v1/crates\",\"api\":\"https://index.example.com\"}";
            Responses["https://index.example.com/3/a/abc"] = $"{{\"name\":\"abc\",\"vers\":\"1.0.0+build\",\"deps\":[],\"cksum\":\"{Cksum}\",\"features\":{{}},\"yanked\":false}}";

            var version = Assert.Single(await Target.ListVersionsAsync(Feed, "abc", CancellationToken.None));

            Assert.Equal("https://index.example.com/api/v1/crates/abc/1.0.0+build/download", version.DownloadUrl);
        }

        [Fact]
        public async Task UsesTheNextMirrorWhenOneDoesNotHaveTheCrate()
        {
            AddMirror("https://first.example.com/");
            AddMirror("https://second.example.com/");
            Responses["https://second.example.com/config.json"] = "{\"dl\":\"https://second.example.com/dl\"}";
            Responses["https://second.example.com/ab/cd/abcd"] = $"{{\"name\":\"abcd\",\"vers\":\"2.0.0\",\"deps\":[],\"cksum\":\"{Cksum}\",\"features\":{{}},\"yanked\":false}}";

            var version = Assert.Single(await Target.ListVersionsAsync(Feed, "abcd", CancellationToken.None));

            Assert.Equal("2.0.0", version.Version);
            Assert.Equal("https://second.example.com/", version.Mirror.PackageSource.AbsoluteUri);
        }
    }

    public class DownloadCrateOrNullAsync : FactsBase
    {
        [Theory]
        [InlineData("https://index.example.com/api/v1/crates/abc/1.0.0/download", true)]
        [InlineData("https://static.example.com/crates/abc/abc-1.0.0.crate", false)]
        public async Task SendsCredentialsOnlyToTheMirrorsOwnUrls(string downloadUrl, bool authenticate)
        {
            var mirror = AddMirror("https://index.example.com/");
            Responses[downloadUrl] = "crate";

            using var crate = await Target.DownloadCrateOrNullAsync(
                Feed,
                new CargoUpstreamVersion { Name = "abc", Version = "1.0.0", DownloadUrl = downloadUrl, Mirror = mirror },
                CancellationToken.None);

            Assert.NotNull(crate);
            Assert.Equal(new[] { authenticate }, Authenticated);
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
                    return new HttpClient(new StubHandler(Responses));
                });

            Target = new CargoUpstreamClient(feedSettings.Object, clients.Object, new UpstreamListingCache(), NullLogger<CargoUpstreamClient>.Instance);
        }

        protected Feed Feed { get; } = new Feed { Id = Guid.NewGuid(), Slug = "rust", Type = FeedType.Cargo };
        protected Dictionary<string, string> Responses { get; } = [];
        protected List<bool> Authenticated { get; } = [];
        protected CargoUpstreamClient Target { get; }

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

        public StubHandler(Dictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var response = _responses.TryGetValue(url, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
