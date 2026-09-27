using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Covers the ETag and Cache-Control headers on the NuGet metadata and icon endpoints.
/// </summary>
public class HttpCachingIntegrationTests
{
    private const string PackageId = "Caching.Test";
    private const string NamedFeedSlug = "team";

    public class RegistrationIndexAsync : FactsBase
    {
        private const string Url = "v3/registration/caching.test/index.json";

        public RegistrationIndexAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ReturnsPrivateNoCacheAndETag()
        {
            await AddPackageAsync("1.0.0");

            using var response = await GetAsync(Url);

            response.EnsureSuccessStatusCode();
            AssertRevalidationHeaders(response);
        }

        [Fact]
        public async Task ReturnsNotModifiedWhenETagMatches()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(etag, response.Headers.ETag);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task ReturnsOkWithNewETagAfterContentChanges()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);
            await AddPackageAsync("2.0.0");

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.NotEqual(etag, response.Headers.ETag);
        }

        [Fact]
        public async Task ReturnsNotModifiedForNamedFeed()
        {
            await _app.CreateFeedAsync(NamedFeedSlug);
            await AddPackageAsync("1.0.0", NamedFeedSlug);
            var url = $"feeds/{NamedFeedSlug}/{Url}";
            var etag = await GetETagAsync(url);

            using var response = await GetAsync(url, etag);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        }

        [Fact]
        public async Task DoesNotReuseETagAcrossFeeds()
        {
            await _app.CreateFeedAsync(NamedFeedSlug);
            await AddPackageAsync("1.0.0");
            await AddPackageAsync("1.0.0", NamedFeedSlug);
            var defaultFeedETag = await GetETagAsync(Url);

            using var response = await GetAsync($"feeds/{NamedFeedSlug}/{Url}", defaultFeedETag);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(defaultFeedETag, response.Headers.ETag);
        }

        [Fact]
        public async Task OmitsETagWhenNotFound()
        {
            using var response = await GetAsync(Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.ETag);
        }
    }

    public class RegistrationPageAsync : FactsBase
    {
        private const string Url = "v3/registration/caching.test/page/1.0.0/2.0.0.json";

        public RegistrationPageAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ReturnsNotModifiedWhenETagMatches()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            AssertRevalidationHeaders(response);
        }
    }

    public class RegistrationLeafAsync : FactsBase
    {
        private const string Url = "v3/registration/caching.test/1.0.0.json";

        public RegistrationLeafAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ReturnsNotModifiedWhenETagMatches()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            AssertRevalidationHeaders(response);
        }
    }

    public class GetPackageVersionsAsync : FactsBase
    {
        private const string Url = "v3/package/caching.test/index.json";

        public GetPackageVersionsAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ReturnsNotModifiedWhenETagMatches()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            AssertRevalidationHeaders(response);
        }

        [Fact]
        public async Task ReturnsOkWithNewETagAfterContentChanges()
        {
            await AddPackageAsync("1.0.0");
            var etag = await GetETagAsync(Url);
            await AddPackageAsync("2.0.0");

            using var response = await GetAsync(Url, etag);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(etag, response.Headers.ETag);
        }
    }

    public class DownloadIconAsync : FactsBase
    {
        private const string Url = "v3/package/caching.test/1.0.0/icon";

        public DownloadIconAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ReturnsPrivateMaxAgeOfOneHour()
        {
            await AddPackageAsync("1.0.0");

            using var response = await GetAsync(Url);

            response.EnsureSuccessStatusCode();
            var cacheControl = response.Headers.CacheControl;
            Assert.NotNull(cacheControl);
            Assert.True(cacheControl.Private);
            Assert.False(cacheControl.Public);
            Assert.Equal(TimeSpan.FromHours(1), cacheControl.MaxAge);
            Assert.DoesNotContain(cacheControl.Extensions, e => e.Name == "immutable");
        }

        [Fact]
        public async Task OmitsCacheControlWhenNotFound()
        {
            using var response = await GetAsync(Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.CacheControl?.MaxAge);
        }
    }

    public class DownloadPackageAsync : FactsBase
    {
        public DownloadPackageAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task LeavesCachingHeadersUnset()
        {
            await AddPackageAsync("1.0.0");

            using var response = await GetAsync("v3/package/caching.test/1.0.0/caching.test.1.0.0.nupkg");

            response.EnsureSuccessStatusCode();
            Assert.Null(response.Headers.ETag);
            Assert.Null(response.Headers.CacheControl);
        }
    }

    public abstract class FactsBase : IDisposable
    {
        // A 1x1 transparent PNG.
        private static readonly byte[] IconBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        protected readonly PaGettoApplication _app;
        protected readonly HttpClient _client;
        private readonly string _iconPath;

        protected FactsBase(ITestOutputHelper output)
        {
            _app = new PaGettoApplication(output);
            _client = _app.CreateDefaultClient();

            // PackageBuilder reads each file more than once, so the icon has to live on disk.
            _iconPath = Path.GetTempFileName();
            File.WriteAllBytes(_iconPath, IconBytes);
        }

        public void Dispose()
        {
            _client.Dispose();
            _app.Dispose();
            File.Delete(_iconPath);
        }

        protected async Task AddPackageAsync(string version, string feedSlug = null)
        {
            var builder = new PackageBuilder
            {
                Id = PackageId,
                Version = NuGetVersion.Parse(version),
                Description = "Test description",
                Icon = "icon.png",
            };
            builder.Authors.Add("Test author");
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = _iconPath,
                TargetPath = "icon.png",
            });

            using var stream = new MemoryStream();
            builder.Save(stream);
            stream.Position = 0;

            if (feedSlug == null)
            {
                await _app.AddPackageAsync(stream);
            }
            else
            {
                await _app.AddPackageToFeedAsync(stream, feedSlug);
            }
        }

        protected async Task<HttpResponseMessage> GetAsync(string url, EntityTagHeaderValue ifNoneMatch = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (ifNoneMatch != null)
            {
                request.Headers.IfNoneMatch.Add(ifNoneMatch);
            }

            return await _client.SendAsync(request);
        }

        protected async Task<EntityTagHeaderValue> GetETagAsync(string url)
        {
            using var response = await GetAsync(url);
            response.EnsureSuccessStatusCode();

            return response.Headers.ETag ?? throw new InvalidOperationException($"No ETag on {url}");
        }

        protected static void AssertRevalidationHeaders(HttpResponseMessage response)
        {
            Assert.NotNull(response.Headers.ETag);

            var cacheControl = response.Headers.CacheControl;
            Assert.NotNull(cacheControl);
            Assert.True(cacheControl.Private);
            Assert.True(cacheControl.NoCache);
            Assert.False(cacheControl.Public);
            Assert.Null(cacheControl.MaxAge);
        }
    }
}
