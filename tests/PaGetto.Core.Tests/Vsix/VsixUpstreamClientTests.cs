using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;
using PaGetto.Core.Vsix;
using Xunit;

namespace PaGetto.Core.Tests.Vsix;

public class VsixUpstreamClientTests
{
    public class GetIndexAsync : FactsBase
    {
        [Fact]
        public async Task TheFirstMirrorThatHasAnExtensionOffersItAndAMirrorThatIsDownIsSkipped()
        {
            AddMirror("https://down.example.com/feed.xml");
            AddMirror("https://one.example.com/feed.xml");
            AddMirror("https://two.example.com/feed.xml");
            Responses["https://one.example.com/feed.xml"] = GalleryFeed(("Hello", "1.0"), ("Hello", "1.2"), ("OnlyOne", "2.0"));
            Responses["https://two.example.com/feed.xml"] = GalleryFeed(("hello", "9.0"), ("OnlyTwo", "3.0"));

            var index = await Target.GetIndexAsync(Feed, CancellationToken.None);

            Assert.Equal(new[] { "hello", "onlyone", "onlytwo" }, index.Keys.Order());

            // A gallery lists one version of an extension; of several, the newest is kept.
            Assert.Equal("1.2", index["hello"].Version.Text);
            Assert.Equal("https://one.example.com/feed.xml", index["hello"].Mirror.PackageSource.AbsoluteUri);
            Assert.Equal("https://one.example.com/Hello-1.2.vsix", index["hello"].Entry.DownloadUrl);
        }

        [Fact]
        public async Task AMirrorThatIsNotAGalleryIsSkipped()
        {
            AddMirror("https://html.example.com/");
            Responses["https://html.example.com/"] = Encoding.UTF8.GetBytes("<html><body>Not a feed</body></html>");

            Assert.Empty(await Target.GetIndexAsync(Feed, CancellationToken.None));
        }
    }

    public class DownloadOrNullAsync : FactsBase
    {
        [Theory]
        [InlineData("https://one.example.com/files/Hello.vsix", true)]
        [InlineData("https://cdn.example.com/files/Hello.vsix", false)]
        public async Task SendsTheMirrorsCredentialsOnlyToItsHost(string downloadUrl, bool authenticate)
        {
            AddMirror("https://one.example.com/feed.xml");
            Responses[downloadUrl] = [1, 2, 3];
            var entry = Entry(downloadUrl);

            using var content = await Target.DownloadOrNullAsync(Feed, entry, 1024, CancellationToken.None);

            Assert.Equal(3, content!.Size);
            Assert.Equal(authenticate, Assert.Single(Authenticated));
        }

        [Fact]
        public async Task ReturnsNullForAFileThatIsTooLargeOrMissing()
        {
            AddMirror("https://one.example.com/feed.xml");
            Responses["https://one.example.com/large.vsix"] = new byte[2048];

            Assert.Null(await Target.DownloadOrNullAsync(Feed, Entry("https://one.example.com/large.vsix"), 1024, CancellationToken.None));
            Assert.Null(await Target.DownloadOrNullAsync(Feed, Entry("https://one.example.com/missing.vsix"), 1024, CancellationToken.None));
        }
    }

    public class GetImageOrNullAsync : FactsBase
    {
        [Fact]
        public async Task PassesThroughAnImageOfAKnownType()
        {
            AddMirror("https://one.example.com/feed.xml");
            Responses["https://one.example.com/icons/Hello.png"] = [1, 2];
            Responses["https://one.example.com/icons/Hello"] = [3, 4];
            ContentTypes["https://one.example.com/icons/Hello"] = "image/jpeg";
            Responses["https://one.example.com/icons/Hello.svg"] = Encoding.UTF8.GetBytes("<svg />");

            var png = await Target.GetImageOrNullAsync(Feed, Entry(iconUrl: "https://one.example.com/icons/Hello.png"), VsixImageKind.Icon, CancellationToken.None);
            var jpeg = await Target.GetImageOrNullAsync(Feed, Entry(iconUrl: "https://one.example.com/icons/Hello"), VsixImageKind.Icon, CancellationToken.None);
            var svg = await Target.GetImageOrNullAsync(Feed, Entry(iconUrl: "https://one.example.com/icons/Hello.svg"), VsixImageKind.Icon, CancellationToken.None);
            var none = await Target.GetImageOrNullAsync(Feed, Entry(iconUrl: "https://one.example.com/icons/Hello.png"), VsixImageKind.PreviewImage, CancellationToken.None);

            Assert.Equal(new byte[] { 1, 2 }, png!.Content);
            Assert.Equal("image/png", png.ContentType);
            Assert.Equal("image/jpeg", jpeg!.ContentType);
            Assert.Null(svg);
            Assert.Null(none);
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
                    return new HttpClient(new StubHandler(Responses, ContentTypes));
                });

            Target = new VsixUpstreamClient(feedSettings.Object, clients.Object, new UpstreamListingCache(), NullLogger<VsixUpstreamClient>.Instance);
        }

        protected Feed Feed { get; } = new Feed { Id = Guid.NewGuid(), Slug = "vs", Type = FeedType.Vsix };
        protected Dictionary<string, byte[]> Responses { get; } = [];
        protected Dictionary<string, string> ContentTypes { get; } = [];
        protected List<bool> Authenticated { get; } = [];
        protected VsixUpstreamClient Target { get; }

        protected void AddMirror(string source)
        {
            _mirrors.Add(new MirrorOptions { Enabled = true, PackageSource = new Uri(source) });
        }

        protected VsixUpstreamEntry Entry(string downloadUrl = "https://one.example.com/Hello.vsix", string iconUrl = null)
        {
            VsixVersion.TryParse("1.0", out var version);

            return new VsixUpstreamEntry
            {
                Entry = new VsixGalleryEntry
                {
                    Manifest = new VsixManifest { Id = "Hello", Version = "1.0" },
                    DownloadUrl = downloadUrl,
                    IconUrl = iconUrl,
                },
                Version = version,
                Mirror = _mirrors[0],
            };
        }

        /// <summary>
        /// A gallery feed with an entry for each identifier and version, with relative file URLs.
        /// </summary>
        protected static byte[] GalleryFeed(params (string Id, string Version)[] extensions)
        {
            var entries = string.Concat(extensions.Select(e => $"""
                <entry>
                  <id>{e.Id}</id>
                  <title type="text">{e.Id}</title>
                  <content type="application/octet-stream" src="{e.Id}-{e.Version}.vsix" />
                  <Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010">
                    <Id>{e.Id}</Id>
                    <Version>{e.Version}</Version>
                  </Vsix>
                </entry>
                """));

            return Encoding.UTF8.GetBytes($"<feed xmlns=\"http://www.w3.org/2005/Atom\">{entries}</feed>");
        }
    }

    /// <summary>
    /// Answers the URLs it knows with their content, URLs of hosts named "down" with 500, and everything else with 404.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses;
        private readonly Dictionary<string, string> _contentTypes;

        public StubHandler(Dictionary<string, byte[]> responses, Dictionary<string, string> contentTypes)
        {
            _responses = responses;
            _contentTypes = contentTypes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (!_responses.TryGetValue(url, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(request.RequestUri.Host.StartsWith("down", StringComparison.Ordinal)
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.NotFound));
            }

            var body = new ByteArrayContent(content);
            if (_contentTypes.TryGetValue(url, out var contentType))
            {
                body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }
}
