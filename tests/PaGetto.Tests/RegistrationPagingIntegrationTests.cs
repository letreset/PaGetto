using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Verifies paged registration indexes, both over raw HTTP and through the official NuGet client.
/// </summary>
public class RegistrationPagingIntegrationTests : IDisposable
{
    private const string PackageId = "Paging.Test";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public RegistrationPagingIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, inMemoryConfiguration: dict =>
        {
            dict["RegistrationPageSize"] = "2";
        });
        _client = _app.CreateDefaultClient();
    }

    [Fact]
    public async Task RegistrationIndex_WhenPageSizeExceeded_LinksToPagesThatResolve()
    {
        await AddVersionsAsync("1.0.0", "2.0.0", "3.0.0");

        using var indexResponse = await _client.GetAsync($"v3/registration/{PackageId.ToLowerInvariant()}/index.json");
        indexResponse.EnsureSuccessStatusCode();

        using var index = JsonDocument.Parse(await indexResponse.Content.ReadAsStringAsync());
        var pages = index.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, index.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.False(page.TryGetProperty("items", out _)));

        var pageUrl = pages[0].GetProperty("@id").GetString();
        Assert.EndsWith("/v3/registration/paging.test/page/1.0.0/2.0.0.json", pageUrl);

        using var pageResponse = await _client.GetAsync(pageUrl);
        pageResponse.EnsureSuccessStatusCode();

        using var page = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
        var versions = page.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("catalogEntry").GetProperty("version").GetString());

        Assert.Equal(new[] { "1.0.0", "2.0.0" }, versions);
    }

    [Fact]
    public async Task RegistrationPage_WhenRangeHasNoPackages_ReturnsNotFound()
    {
        await AddVersionsAsync("1.0.0");

        using var response = await _client.GetAsync($"v3/registration/{PackageId.ToLowerInvariant()}/page/5.0.0/6.0.0.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NuGetClient_ReadsAllVersionsFromPagedRegistration()
    {
        await AddVersionsAsync("1.0.0", "2.0.0", "3.0.0");

        var sourceUri = new Uri(_app.Server.BaseAddress, "v3/index.json");
        var providers = new List<Lazy<INuGetResourceProvider>>
        {
            new(() => new HttpSourceResourceProviderTestHost(_client)),
        };
        providers.AddRange(Repository.Provider.GetCoreV3());
        var repository = new SourceRepository(new PackageSource(sourceUri.AbsoluteUri), providers);

        using var cache = new SourceCacheContext { NoCache = true, MaxAge = new DateTimeOffset(), DirectDownload = true };
        var resource = await repository.GetResourceAsync<PackageMetadataResource>();
        var packages = await resource.GetMetadataAsync(
            PackageId,
            includePrerelease: true,
            includeUnlisted: true,
            cache,
            NuGet.Common.NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(
            new[] { "1.0.0", "2.0.0", "3.0.0" },
            packages.Select(p => p.Identity.Version.ToNormalizedString()).OrderBy(v => v));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task AddVersionsAsync(params string[] versions)
    {
        foreach (var version in versions)
        {
            var builder = new PackageBuilder
            {
                Id = PackageId,
                Version = NuGetVersion.Parse(version),
                Description = "Test description",
            };
            builder.Authors.Add("Test author");
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = GetType().Assembly.Location,
                TargetPath = "lib/Test.dll",
            });

            using var stream = new MemoryStream();
            builder.Save(stream);
            stream.Position = 0;

            await _app.AddPackageAsync(stream);
        }
    }
}
