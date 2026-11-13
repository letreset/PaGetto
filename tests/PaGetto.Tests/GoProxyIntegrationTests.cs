using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The module proxy of a Go feed, in Legacy mode without credentials (open access).
/// </summary>
public class GoProxyIntegrationTests : IDisposable
{
    private const string Feed = "golang";
    private const string Module = "example.com/Team/tool";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public GoProxyIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task UploadedModuleIsServedToTheGoCommand()
    {
        await _app.CreateGoFeedAsync(Feed);
        var zip = GoModuleZipBuilder.Create(Module, "v1.0.0", readme: "# Tool");

        using var upload = await _client.UploadModuleAsync(Feed, Module, "v1.0.0", zip);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        using (await _client.UploadModuleAsync(Feed, Module, "v1.1.0-rc.1"))
        {
        }

        // The go command asks with the path escaped: example.com/!team/tool.
        Assert.Equal("v1.0.0\nv1.1.0-rc.1\n", await _client.GetStringAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list")));

        using (var info = JsonDocument.Parse(await _client.GetStringAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".info"))))
        {
            Assert.Equal("v1.0.0", info.RootElement.GetProperty("Version").GetString());
            Assert.Equal("2020-01-01T00:00:00Z", info.RootElement.GetProperty("Time").GetString());
        }

        Assert.Equal(GoModuleZipBuilder.GoMod(Module), await _client.GetStringAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".mod")));

        using var download = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".zip"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(zip, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("v1.0.0");
        Assert.Equal("example.com/!team/tool", stored.Artifact.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip)), stored.ContentHash);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task FolderEntriesOfAnUploadedZipAreLeftOut()
    {
        await _app.CreateGoFeedAsync(Feed);
        var folder = $"{Module}@v1.0.0/";
        var zip = GoModuleZipBuilder.CreateZip(new Dictionary<string, string>
        {
            [folder] = "",
            [folder + "go.mod"] = GoModuleZipBuilder.GoMod(Module),
            [folder + "sub/"] = "",
            [folder + "sub/a.go"] = "package sub\n",
        });

        using (var upload = await _client.UploadModuleAsync(Feed, Module, "v1.0.0", zip))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var served = await _client.GetByteArrayAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".zip"));

        using var archive = new ZipArchive(new MemoryStream(served));
        Assert.Equal(new[] { folder + "go.mod", folder + "sub/a.go" }, archive.Entries.Select(e => e.FullName));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(served)), (await GetVersionAsync("v1.0.0")).ContentHash);
    }

    [Fact]
    public async Task LatestIsTheNewestRelease()
    {
        await _app.CreateGoFeedAsync(Feed);
        foreach (var version in new[] { "v1.0.0", "v1.2.0", "v1.10.0-rc.1", "v0.0.0-20191109021931-daa7c04131f5" })
        {
            using (await _client.UploadModuleAsync(Feed, Module, version))
            {
            }
        }

        Assert.Equal("v1.2.0", await GetLatestVersionAsync());

        // Pseudo-versions are left out of the list.
        Assert.Equal("v1.0.0\nv1.2.0\nv1.10.0-rc.1\n", await _client.GetStringAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list")));
    }

    [Fact]
    public async Task ModuleWithOnlyPseudoVersionsHasAnEmptyListAndALatest()
    {
        await _app.CreateGoFeedAsync(Feed);
        using (await _client.UploadModuleAsync(Feed, Module, "v0.0.0-20191109021931-daa7c04131f5"))
        {
        }

        using (await _client.UploadModuleAsync(Feed, Module, "v0.0.0-20200101000000-0123456789ab"))
        {
        }

        using var list = await _client.GetAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list"));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("", await list.Content.ReadAsStringAsync());
        Assert.Equal("v0.0.0-20200101000000-0123456789ab", await GetLatestVersionAsync());
    }

    [Fact]
    public async Task UnlistedVersionIsLeftOutOfTheListButStillServed()
    {
        await _app.CreateGoFeedAsync(Feed);
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0"))
        {
        }

        using (await _client.UploadModuleAsync(Feed, Module, "v1.1.0"))
        {
        }

        await SetListedAsync("v1.1.0", listed: false);

        Assert.Equal("v1.0.0\n", await _client.GetStringAsync(GoTestClient.ProxyUrl(Feed, Module, "@v/list")));
        Assert.Equal("v1.0.0", await GetLatestVersionAsync());

        using var zip = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.1.0", ".zip"));
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
    }

    [Theory]
    [InlineData("@v/list")]
    [InlineData("@latest")]
    [InlineData("@v/v1.0.0.info")]
    [InlineData("@v/v1.0.0.mod")]
    [InlineData("@v/v1.0.0.zip")]
    [InlineData("@v/master.info")]
    public async Task UnknownModuleIsNotFound(string file)
    {
        await _app.CreateGoFeedAsync(Feed);

        using var response = await _client.GetAsync(GoTestClient.ProxyUrl(Feed, "example.com/unknown", file));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("feeds/golang/go/example.com/Team/tool/@v/list")]
    [InlineData("feeds/golang/go/example.com/!team/tool/@v/v1.0.0.tar.gz")]
    [InlineData("feeds/golang/go/example.com/!team/tool")]
    [InlineData("feeds/golang/go/")]
    public async Task OtherPathsAreNotFound(string path)
    {
        await _app.CreateGoFeedAsync(Feed);
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0"))
        {
        }

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VersionsAreCaseSensitive()
    {
        await _app.CreateGoFeedAsync(Feed);
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0-RC1"))
        {
        }

        using var exact = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0-RC1", ".mod"));
        using var other = await _client.GetAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0-rc1", ".mod"));

        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task UploadingAnExistingVersionIsRejected()
    {
        await _app.CreateGoFeedAsync(Feed);
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0"))
        {
        }

        using var response = await _client.UploadModuleAsync(Feed, Module, "v1.0.0");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OverwritingFeedReplacesTheVersion()
    {
        await _app.CreateGoFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = PackageOverwriteAllowed.True);
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0"))
        {
        }

        var replacement = GoModuleZipBuilder.Create(Module, "v1.0.0", readme: "changed");
        using var response = await _client.UploadModuleAsync(Feed, Module, "v1.0.0", replacement);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(replacement, await _client.GetByteArrayAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".zip")));
    }

    [Theory]
    [InlineData("tool", "v1.0.0", "not a valid module path")]
    [InlineData("example.com/tool", "1.0.0", "not a canonical module version")]
    [InlineData("example.com/tool", "v2.0.0", "no /v2 suffix")]
    [InlineData("example.com/tool/v2", "v1.0.0", "must be v2")]
    public async Task InvalidPathOrVersionIsRejectedWithAMessage(string module, string version, string message)
    {
        await _app.CreateGoFeedAsync(Feed);

        using var response = await _client.UploadModuleAsync(Feed, module, version, GoModuleZipBuilder.Create(module, version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> InvalidZips()
    {
        yield return [GoModuleZipBuilder.Create(Module, "v2.0.0"), "not in the folder"];
        yield return [GoModuleZipBuilder.Create(Module, "v1.0.0", goMod: "module example.com/other\n"), "declares the module example.com/other"];
        yield return [GoModuleZipBuilder.CreateZip(new Dictionary<string, string> { [$"{Module}@v1.0.0/a.go"] = "package a\n" }), "no example.com/Team/tool@v1.0.0/go.mod"];
        yield return [new byte[] { 1, 2, 3 }, "not a valid zip"];
    }

    [Theory]
    [MemberData(nameof(InvalidZips))]
    public async Task InvalidZipIsRejectedWithAMessage(byte[] zip, string message)
    {
        await _app.CreateGoFeedAsync(Feed);

        using var response = await _client.UploadModuleAsync(Feed, Module, "v1.0.0", zip);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyZipsCanBeUploaded()
    {
        await _app.CreateGoFeedAsync(Feed);

        using var content = new ByteArrayContent(GoModuleZipBuilder.Create(Module, "v1.0.0"));
        using var response = await _client.PutAsync(GoTestClient.VersionUrl(Feed, Module, "v1.0.0", ".mod"), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesUploads()
    {
        await _app.CreateGoFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.UploadModuleAsync(Feed, Module, "v1.0.0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ZipLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateGoFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress, so the zip stays above 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(1024 * 1024));
        using var response = await _client.UploadModuleAsync(Feed, Module, "v1.0.0", GoModuleZipBuilder.Create(Module, "v1.0.0", readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToUpload()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateGoFeedAsync(Feed);

        using var anonymous = await client.UploadModuleAsync(Feed, Module, "v1.0.0");

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.UploadModuleAsync(Feed, Module, "v1.0.0");

        client.UseBasicCredentials("ci", "secret-key");
        using var right = await client.UploadModuleAsync(Feed, Module, "v1.0.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "api/packages/tool")]
    [InlineData("GET", "npm/tool")]
    [InlineData("GET", "cargo/index/config.json")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnAGoFeed(string method, string path)
    {
        await _app.CreateGoFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "go/example.com/tool/@v/list")]
    [InlineData("GET", "go/example.com/tool/@latest")]
    [InlineData("PUT", "go/example.com/tool/@v/v1.0.0.zip")]
    public async Task GoApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateCargoFeedAsync("rust");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/rust/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsModules()
    {
        await _app.CreateGoFeedAsync(Feed);

        var goMod = $"module {Module}\n\ngo 1.22\n\nrequire (\n\tgolang.org/x/mod v0.17.0\n\tgolang.org/x/text v0.14.0 // indirect\n)\n";
        using (await _client.UploadModuleAsync(Feed, Module, "v1.0.0", GoModuleZipBuilder.Create(Module, "v1.0.0", "# Hello from the readme\n\n<script>alert(1)</script>", goMod)))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("Go feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">Go</span>", index);
        Assert.Contains(Module, index);
        Assert.Contains($"go get {Module}@v1.0.0", index);
        Assert.Contains(">v1.0.0<", index);
        Assert.DoesNotContain("vv1.0.0", index);

        // Routing lowercases links, so they name the module by its escaped path, which keeps its case.
        var link = Regex.Match(index, $"href=\"(/feeds/{Feed}/artifacts/[^\"]+)\"").Groups[1].Value;
        Assert.Contains($"<h1>{Module}</h1>", await _client.GetStringAsync(WebUtility.HtmlDecode(link)));

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/{Uri.EscapeDataString(Module)}");
        var pageLinks = Regex.Matches(details, $"(?:href|action)=\"(/feeds/{Feed}/artifacts/example[^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(pageLinks);
        Assert.All(pageLinks, l => Assert.Contains("!team", WebUtility.HtmlDecode(Uri.UnescapeDataString(l))));
        Assert.Contains($"<h1>{Module}</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("Go 1.22", details);
        Assert.Contains("golang.org/x/mod", details);
        Assert.DoesNotContain("golang.org/x/text", details);
        Assert.Contains($"require {Module} v1.0.0", details);
        Assert.Contains($"/feeds/{Feed}/go/example.com/!team/tool/@v/v1.0.0.zip", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/go", connect);
        Assert.Contains("GONOSUMDB", connect);
        Assert.Contains("proxy.golang.org", connect);
    }

    private async Task<string> GetLatestVersionAsync()
    {
        using var latest = JsonDocument.Parse(await _client.GetStringAsync(GoTestClient.ProxyUrl(Feed, Module, "@latest")));
        return latest.RootElement.GetProperty("Version").GetString();
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

        var stored = await context.ArtifactVersions.SingleAsync(v => v.Version == version);
        stored.Listed = listed;
        await context.SaveChangesAsync(default);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
