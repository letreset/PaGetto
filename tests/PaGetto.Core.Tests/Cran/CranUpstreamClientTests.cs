using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranUpstreamClientTests
{
    public class GetIndexAsync : FactsBase
    {
        [Fact]
        public async Task ReadsTheGzippedIndexAndKeepsTheNewestEntryOfAPackage()
        {
            // As CRAN lists its recommended packages: at the top, and for each R version in a folder.
            AddMirror("https://cran.example.com");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Gzip("""
                Package: MASS
                Version: 7.3-61
                MD5sum: aaaa

                Package: MASS
                Version: 7.3-61
                Path: 4.4.1/Recommended
                MD5sum: bbbb

                Package: MASS
                Version: 7.3-60
                Path: 4.3.3/Recommended

                Package: zoo
                Version: 1.8-12
                Depends: R (>= 3.1.0),
                    stats

                Package: not_valid
                Version: 1.0
                """);

            var index = await Target.GetIndexAsync(Feed, CancellationToken.None);

            Assert.Equal(new[] { "mass", "zoo" }, index.Keys.Order());
            Assert.Equal("MASS_7.3-61.tar.gz", index["mass"].Path);
            Assert.Equal("aaaa", index["mass"].Md5);
            Assert.Equal("R (>= 3.1.0),\nstats", CranDcf.GetValue(index["zoo"].IndexEntry, "Depends"));
        }

        [Fact]
        public async Task ThePackageOfTheFirstMirrorWinsAndMirrorsThatFailAreSkipped()
        {
            AddMirror("https://down.example.com");
            AddMirror("https://first.example.com");
            AddMirror("https://second.example.com");
            Responses["https://first.example.com/src/contrib/PACKAGES.gz"] = Gzip("Package: zoo\nVersion: 1.0\n");
            Responses["https://second.example.com/src/contrib/PACKAGES.gz"] = Gzip("Package: zoo\nVersion: 2.0\n\nPackage: xts\nVersion: 0.1\n");

            var index = await Target.GetIndexAsync(Feed, CancellationToken.None);

            Assert.Equal("1.0", index["zoo"].Version.Text);
            Assert.Equal("https://first.example.com/", index["zoo"].Mirror.PackageSource.AbsoluteUri);
            Assert.Equal("0.1", index["xts"].Version.Text);
        }

        [Fact]
        public async Task ReadsAnIndexThatIsNotCompressed()
        {
            // HttpClient decompresses an index sent with a gzip content encoding.
            AddMirror("https://cran.example.com/");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Encoding.UTF8.GetBytes("Package: zoo\nVersion: 1.0\n");

            Assert.Single(await Target.GetIndexAsync(Feed, CancellationToken.None));
        }
    }

    public class ListVersionsAsync : FactsBase
    {
        [Fact]
        public async Task ListsTheArchiveAndTheCurrentVersion()
        {
            AddMirror("https://cran.example.com");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Gzip("Package: Zoo\nVersion: 1.2\n");
            Responses["https://cran.example.com/src/contrib/Archive/Zoo/"] = Encoding.UTF8.GetBytes("""
                <html><body>
                <a href="?C=N;O=D">Name</a> <a href="/src/contrib/Archive/">Parent Directory</a>
                <a href="Zoo_1.0.tar.gz">Zoo_1.0.tar.gz</a>
                <a href="Zoo_1.1-1.tar.gz">Zoo_1.1-1.tar.gz</a>
                <a href="Zoo_1.2.tar.gz">Zoo_1.2.tar.gz</a>
                <a href="Other_1.0.tar.gz">Other_1.0.tar.gz</a>
                </body></html>
                """);

            var versions = await Target.ListVersionsAsync(Feed, "zoo", CancellationToken.None);

            Assert.Equal(new[] { "1.0", "1.1-1", "1.2" }, versions.Select(v => v.Version.Text));
            Assert.Equal(new[] { "Archive/Zoo/Zoo_1.0.tar.gz", "Archive/Zoo/Zoo_1.1-1.tar.gz", "Zoo_1.2.tar.gz" }, versions.Select(v => v.Path));
            Assert.NotNull(versions[^1].IndexEntry);
        }

        [Fact]
        public async Task ListsTheArchiveOfAPackageThatIsNoLongerInTheIndex()
        {
            AddMirror("https://cran.example.com");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Gzip("Package: zoo\nVersion: 1.2\n");
            Responses["https://cran.example.com/src/contrib/Archive/gone/"] = Encoding.UTF8.GetBytes("<a href=\"gone_0.1.tar.gz\">gone_0.1.tar.gz</a>");

            var version = Assert.Single(await Target.ListVersionsAsync(Feed, "gone", CancellationToken.None));

            Assert.Equal("Archive/gone/gone_0.1.tar.gz", version.Path);
            Assert.Null(version.Md5);
        }

        [Fact]
        public async Task UnknownPackageHasNoVersions()
        {
            AddMirror("https://cran.example.com");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Gzip("Package: zoo\nVersion: 1.2\n");

            Assert.Empty(await Target.ListVersionsAsync(Feed, "unknown", CancellationToken.None));
        }
    }

    public class DownloadOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ChecksTheMd5OfTheIndex()
        {
            var package = Encoding.UTF8.GetBytes("package");
            var md5 = Convert.ToHexStringLower(MD5.HashData(package));
            AddMirror("https://cran.example.com");
            Responses["https://cran.example.com/src/contrib/PACKAGES.gz"] = Gzip($"Package: good\nVersion: 1.0\nMD5sum: {md5}\n\nPackage: bad\nVersion: 1.0\nMD5sum: 0000\n");
            Responses["https://cran.example.com/src/contrib/good_1.0.tar.gz"] = package;
            Responses["https://cran.example.com/src/contrib/bad_1.0.tar.gz"] = package;
            var index = await Target.GetIndexAsync(Feed, CancellationToken.None);

            using var good = await Target.DownloadOrNullAsync(Feed, index["good"], 1024, CancellationToken.None);
            using var bad = await Target.DownloadOrNullAsync(Feed, index["bad"], 1024, CancellationToken.None);

            Assert.Equal(md5, good!.Md5);
            Assert.Null(bad);
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

            Target = new CranUpstreamClient(feedSettings.Object, clients.Object, new UpstreamListingCache(), NullLogger<CranUpstreamClient>.Instance);
        }

        protected Feed Feed { get; } = new Feed { Id = Guid.NewGuid(), Slug = "r", Type = FeedType.Cran };
        protected Dictionary<string, byte[]> Responses { get; } = [];
        protected CranUpstreamClient Target { get; }

        protected void AddMirror(string source)
        {
            _mirrors.Add(new MirrorOptions { Enabled = true, PackageSource = new Uri(source) });
        }

        protected static byte[] Gzip(string text)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            {
                gzip.Write(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")));
            }

            return output.ToArray();
        }
    }

    /// <summary>
    /// Answers the URLs it knows with their content, URLs of hosts named "down" with 500, and everything else with 404.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses;

        public StubHandler(Dictionary<string, byte[]> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var response = _responses.TryGetValue(url, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(request.RequestUri.Host.StartsWith("down", StringComparison.Ordinal) ? HttpStatusCode.InternalServerError : HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
