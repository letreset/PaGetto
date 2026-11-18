using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The CRAN repository of a CRAN feed, in Legacy mode without credentials (open access).
/// </summary>
public class CranRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "r";
    private const string Package = "hello";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public CranRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task UploadedPackageIsServedToR()
    {
        await _app.CreateCranFeedAsync(Feed);
        var package = CranPackageBuilder.Create(Package, "1.0.0", readme: "# Hello");

        using var upload = await _client.UploadCranPackageAsync(Feed, Package, "1.0.0", package);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var entry = (await _client.GetCranIndexAsync(Feed))[Package];
        Assert.Equal("1.0.0", CranDcf.GetValue(entry, "Version"));
        Assert.Equal("R (>= 4.1.0)", CranDcf.GetValue(entry, "Depends"));
        Assert.Equal("rlang (>= 1.0.0), cli", CranDcf.GetValue(entry, "Imports"));
        Assert.Equal(Convert.ToHexStringLower(MD5.HashData(package)), CranDcf.GetValue(entry, "MD5sum"));
        Assert.Null(CranDcf.GetValue(entry, "Title"));

        // The same index, not compressed.
        var plain = await _client.GetStringAsync($"feeds/{Feed}/cran/src/contrib/PACKAGES");
        Assert.StartsWith("Package: hello\nVersion: 1.0.0\n", plain);

        using var download = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello_1.0.0.tar.gz");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(package, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal(Package, stored.Artifact.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(package)), stored.ContentHash);
        Assert.Equal("A Test Package", stored.Description);
        Assert.False(stored.IsPrerelease);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task IndexHasTheNewestListedVersionAndTheArchiveTheOthers()
    {
        await _app.CreateCranFeedAsync(Feed);
        foreach (var version in new[] { "1.0", "1.10", "1.9-1" })
        {
            using var upload = await _client.UploadCranPackageAsync(Feed, Package, version);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        Assert.Equal("1.10", CranDcf.GetValue((await _client.GetCranIndexAsync(Feed))[Package], "Version"));

        var listing = await _client.GetStringAsync($"feeds/{Feed}/cran/src/contrib/Archive/{Package}/");
        Assert.Matches("hello_1.0.tar.gz(.|\n)*hello_1.9-1.tar.gz", listing);
        Assert.DoesNotContain("hello_1.10.tar.gz", listing);

        // remotes and renv download older versions from the archive.
        using var archived = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/Archive/{Package}/hello_1.0.tar.gz");
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);

        // An unlisted version is left out of the index, but can still be downloaded.
        await SetListedAsync("1.10", listed: false);
        Assert.Equal("1.9-1", CranDcf.GetValue((await _client.GetCranIndexAsync(Feed))[Package], "Version"));
        using var unlisted = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello_1.10.tar.gz");
        Assert.Equal(HttpStatusCode.OK, unlisted.StatusCode);
    }

    [Fact]
    public async Task NamesAreMatchedRegardlessOfCaseAndKeepTheirPublishedCase()
    {
        await _app.CreateCranFeedAsync(Feed);
        using (var upload = await _client.UploadCranPackageAsync(Feed, "Hello.World", "1.0"))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var index = await _client.GetCranIndexAsync(Feed);
        using var download = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello.world_1.0.tar.gz");
        using var again = await _client.UploadCranPackageAsync(Feed, "hello.world", "1.0");

        Assert.True(index.ContainsKey("Hello.World"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("hello.world", (await GetVersionAsync("1.0")).Artifact.Name);
    }

    [Fact]
    public async Task VersionsAreMatchedByTheirNumbers()
    {
        await _app.CreateCranFeedAsync(Feed);
        using (await _client.UploadCranPackageAsync(Feed, Package, "1.0-2"))
        {
        }

        using var again = await _client.UploadCranPackageAsync(Feed, Package, "1.0.2");
        using var download = await _client.GetAsync($"feeds/{Feed}/cran/src/contrib/hello_1.00.2.tar.gz");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Version 1.0-2 of package hello already exists", await again.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Theory]
    [InlineData(PackageOverwriteAllowed.True, HttpStatusCode.Created)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.False, HttpStatusCode.Conflict)]
    public async Task OnlyAFeedThatAllowsEveryOverwriteReplacesAVersion(PackageOverwriteAllowed allow, HttpStatusCode expected)
    {
        await _app.CreateCranFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = allow);
        using (await _client.UploadCranPackageAsync(Feed, Package, "1.0"))
        {
        }

        var replacement = CranPackageBuilder.Create(Package, "1.0", readme: "changed");
        using var response = await _client.UploadCranPackageAsync(Feed, Package, "1.0", replacement);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Created)
        {
            Assert.Equal(replacement, await _client.GetByteArrayAsync($"feeds/{Feed}/cran/src/contrib/hello_1.0.tar.gz"));
        }
    }

    public static IEnumerable<object[]> InvalidUploads()
    {
        yield return ["hello.tar.gz", CranPackageBuilder.Create(Package, "1.0"), "not the file name of a source package"];
        yield return ["hello_1.0.zip", CranPackageBuilder.Create(Package, "1.0"), "not the file name of a source package"];
        yield return ["hello_2.0.tar.gz", CranPackageBuilder.Create(Package, "1.0"), "doesn't match the package's DESCRIPTION file, which has hello_1.0.tar.gz"];
        yield return ["other_1.0.tar.gz", CranPackageBuilder.Create(Package, "1.0"), "doesn't match"];
        yield return ["hello_1.0.tar.gz", CranPackageBuilder.Create(Package, "1.0", folder: "hello-1.0"), "is in the folder hello-1.0"];
        yield return ["hello_1.0.tar.gz", new byte[] { 1, 2, 3 }, "not a valid gzipped tar archive"];
    }

    [Theory]
    [MemberData(nameof(InvalidUploads))]
    public async Task InvalidUploadIsRejectedWithAMessage(string fileName, byte[] package, string message)
    {
        await _app.CreateCranFeedAsync(Feed);

        using var response = await _client.UploadCranFileAsync(Feed, fileName, package);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesUploads()
    {
        await _app.CreateCranFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.UploadCranPackageAsync(Feed, Package, "1.0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PackageLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateCranFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the package stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var response = await _client.UploadCranPackageAsync(Feed, Package, "1.0", CranPackageBuilder.Create(Package, "1.0", readme: readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToUpload()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateCranFeedAsync(Feed);

        using var anonymous = await client.UploadCranPackageAsync(Feed, Package, "1.0");

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.UploadCranPackageAsync(Feed, Package, "1.0");

        client.UseBasicCredentials("ci", "secret-key");
        using var right = await client.UploadCranPackageAsync(Feed, Package, "1.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Theory]
    [InlineData("bin/windows/contrib/4.4/PACKAGES")]
    [InlineData("bin/macosx/big-sur-arm64/contrib/4.4/PACKAGES")]
    public async Task IndexesOfBinaryPackagesAreEmpty(string path)
    {
        await _app.CreateCranFeedAsync(Feed);
        using (await _client.UploadCranPackageAsync(Feed, Package, "1.0"))
        {
        }

        using var plain = await _client.GetAsync($"feeds/{Feed}/cran/{path}");
        var compressed = await _client.GetByteArrayAsync($"feeds/{Feed}/cran/{path}.gz");

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Empty(await plain.Content.ReadAsByteArrayAsync());
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        Assert.Equal(-1, gzip.ReadByte());
    }

    [Theory]
    [InlineData("cran/src/contrib/PACKAGES.rds")]
    [InlineData("cran/src/contrib/hello_2.0.tar.gz")]
    [InlineData("cran/src/contrib/unknown_1.0.tar.gz")]
    [InlineData("cran/src/contrib/hello_1.0.zip")]
    [InlineData("cran/src/contrib/Archive/unknown/")]
    [InlineData("cran/src/contrib/Archive/hello/other_1.0.tar.gz")]
    [InlineData("cran/bin/windows/contrib/4.4/hello_1.0.zip")]
    [InlineData("cran/src/contrib/Archive/not_valid/")]
    public async Task UnknownPackagesAndOtherPathsAreNotFound(string path)
    {
        await _app.CreateCranFeedAsync(Feed);
        using (await _client.UploadCranPackageAsync(Feed, Package, "1.0"))
        {
        }

        using (await _client.UploadCranPackageAsync(Feed, "other", "1.0"))
        {
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "composer/packages.json")]
    [InlineData("GET", "hex/names")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnACranFeed(string method, string path)
    {
        await _app.CreateCranFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "cran/src/contrib/PACKAGES.gz")]
    [InlineData("GET", "cran/src/contrib/hello_1.0.tar.gz")]
    [InlineData("GET", "cran/src/contrib/Archive/hello/")]
    [InlineData("GET", "cran/bin/windows/contrib/4.4/PACKAGES")]
    [InlineData("PUT", "cran/src/contrib/hello_1.0.tar.gz")]
    public async Task CranApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateComposerFeedAsync("php");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/php/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsPackages()
    {
        await _app.CreateCranFeedAsync(Feed);

        var description = CranPackageBuilder.Description("Hello", "1.0-2", "URL: https://github.com/acme/hello\nBugReports: https://github.com/acme/hello/issues");
        var package = CranPackageBuilder.Create("Hello", "1.0-2", readme: "# Hello from the readme\n\n<script>alert(1)</script>", description: description);
        using (await _client.UploadCranPackageAsync(Feed, "Hello", "1.0-2", package))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("R feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">R</span>", index);
        Assert.Contains("Hello", index);
        Assert.Contains($"install.packages(&quot;Hello&quot;, repos = &quot;http://localhost/feeds/{Feed}/cran&quot;)", index);

        var link = Regex.Match(index, $"href=\"(/feeds/{Feed}/artifacts/[^\"]+)\"").Groups[1].Value;
        Assert.Contains("<h1>Hello</h1>", await _client.GetStringAsync(WebUtility.HtmlDecode(link)));

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/hello");
        Assert.Contains("<h1>Hello</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("R &gt;= 4.1.0", details);
        Assert.Contains("rlang", details);
        Assert.Contains("MIT &#x2B; file LICENSE", details);
        Assert.Contains("Ada Lovelace &lt;ada@example.com&gt;", details);
        Assert.Contains("https://github.com/acme/hello/issues", details);
        Assert.Contains($"/feeds/{Feed}/cran/src/contrib/Hello_1.0-2.tar.gz", details);
        Assert.Contains("renv::install(", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/cran", connect);
        Assert.Contains("options(repos", connect);
        Assert.Contains("src/contrib/", connect);
    }

    private async Task<ArtifactVersion> GetVersionAsync(string version)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .SingleAsync(v => v.Version == version);
    }

    private async Task SetListedAsync(string version, bool listed)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        var stored = await context.ArtifactVersions.Include(v => v.Artifact).SingleAsync(v => v.Version == version);

        // Through the artifact database, as the web UI does, so the feed's cached indexes are rebuilt.
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        Assert.True(await artifacts.SetListedAsync(stored.Artifact.FeedId, stored.Artifact.Name, version, listed, default));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
