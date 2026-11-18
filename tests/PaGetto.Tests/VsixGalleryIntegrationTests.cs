using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.Vsix;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The gallery of a VSIX feed, in Legacy mode without credentials (open access).
/// </summary>
public class VsixGalleryIntegrationTests : IDisposable
{
    private const string Feed = "vs";
    private const string Id = "Contoso.HelloTools";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public VsixGalleryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task UploadedExtensionIsInTheGallery()
    {
        await _app.CreateVsixFeedAsync(Feed);
        var vsix = VsixPackageBuilder.Create(Id, "1.0.0");

        using var upload = await _client.UploadVsixAsync(Feed, Id, "1.0.0", vsix);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        using var gallery = await _client.GetAsync($"feeds/{Feed}/vsix/atom.xml");
        Assert.Equal("application/atom+xml", gallery.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Feed, XDocument.Parse(await gallery.Content.ReadAsStringAsync()).Root!.Element(XName.Get("title", "http://www.w3.org/2005/Atom"))!.Value);

        var entry = (await _client.GetVsixGalleryAsync(Feed))[Id];
        Assert.Equal("1.0.0", entry.Manifest.Version);
        Assert.Equal("Hello Tools", entry.Manifest.DisplayName);
        Assert.Equal("Says hello from the editor.", entry.Manifest.Description);
        Assert.Equal("Contoso", entry.Manifest.Publisher);
        Assert.Equal(2, entry.Manifest.InstallationTargets.Count);
        Assert.Equal(0, entry.Downloads);
        Assert.Equal($"http://localhost/feeds/{Feed}/vsix/extensions/{Id}/1.0.0/{Id}-1.0.0.vsix", entry.DownloadUrl);
        Assert.Equal($"http://localhost/feeds/{Feed}/vsix/extensions/{Id}/1.0.0/icon", entry.IconUrl);
        Assert.Equal($"http://localhost/feeds/{Feed}/vsix/extensions/{Id}/1.0.0/preview", entry.PreviewImageUrl);

        using var download = await _client.GetAsync(entry.DownloadUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(vsix, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal($"{Id}-1.0.0.vsix", download.Content.Headers.ContentDisposition!.FileName);

        using var icon = await _client.GetAsync(entry.IconUrl);
        Assert.Equal("image/png", icon.Content.Headers.ContentType!.MediaType);
        Assert.Equal(VsixPackageBuilder.Icon, await icon.Content.ReadAsByteArrayAsync());
        Assert.Equal(VsixPackageBuilder.PreviewImage, await _client.GetByteArrayAsync(entry.PreviewImageUrl));

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal("contoso.hellotools", stored.Artifact.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(vsix)), stored.ContentHash);
        Assert.Equal(vsix.Length, stored.Size);
        Assert.Equal("Says hello from the editor.", stored.Description);
        Assert.False(stored.IsPrerelease);
        Assert.Equal(1, stored.Downloads);
    }

    [Fact]
    public async Task GalleryHasTheNewestListedVersion()
    {
        await _app.CreateVsixFeedAsync(Feed);
        foreach (var version in new[] { "1.0", "1.10", "1.9.1" })
        {
            using var upload = await _client.UploadVsixAsync(Feed, Id, version);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        Assert.Equal("1.10", (await _client.GetVsixGalleryAsync(Feed))[Id].Manifest.Version);

        // An unlisted version is left out of the gallery, but can still be downloaded.
        await SetListedAsync("1.10", listed: false);
        Assert.Equal("1.9.1", (await _client.GetVsixGalleryAsync(Feed))[Id].Manifest.Version);
        using var unlisted = await _client.GetAsync($"feeds/{Feed}/vsix/extensions/{Id}/1.10/{Id}-1.10.vsix");
        Assert.Equal(HttpStatusCode.OK, unlisted.StatusCode);

        // An extension without listed versions is left out.
        await SetListedAsync("1.9.1", listed: false);
        await SetListedAsync("1.0", listed: false);
        Assert.Empty(await _client.GetVsixGalleryAsync(Feed));
    }

    [Fact]
    public async Task IdentifiersAreMatchedRegardlessOfCaseAndVersionsWithoutLeadingZeros()
    {
        await _app.CreateVsixFeedAsync(Feed);
        using (var upload = await _client.UploadVsixAsync(Feed, Id, "1.02"))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var download = await _client.GetAsync($"feeds/{Feed}/vsix/extensions/contoso.hellotools/1.2/any-name.vsix");
        using var again = await _client.UploadVsixAsync(Feed, "CONTOSO.HELLOTOOLS", "1.2");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Version 1.2 of extension CONTOSO.HELLOTOOLS already exists", await again.Content.ReadAsStringAsync());
        Assert.Equal("1.2", (await GetVersionAsync("1.2")).Version);
    }

    [Theory]
    [InlineData(PackageOverwriteAllowed.True, HttpStatusCode.Created)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.False, HttpStatusCode.Conflict)]
    public async Task OnlyAFeedThatAllowsEveryOverwriteReplacesAVersion(PackageOverwriteAllowed allow, HttpStatusCode expected)
    {
        await _app.CreateVsixFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = allow);
        using (await _client.UploadVsixAsync(Feed, Id, "1.0"))
        {
        }

        var replacement = VsixPackageBuilder.Create(Id, "1.0", extraFiles: new Dictionary<string, byte[]> { ["changed.txt"] = [1] });
        using var response = await _client.UploadVsixAsync(Feed, Id, "1.0", replacement);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Created)
        {
            Assert.Equal(replacement, await _client.GetByteArrayAsync($"feeds/{Feed}/vsix/extensions/{Id}/1.0/{Id}-1.0.vsix"));
        }
    }

    [Fact]
    public async Task UploadWithPostIsAcceptedToo()
    {
        await _app.CreateVsixFeedAsync(Feed);
        var content = new ByteArrayContent(VsixPackageBuilder.Create(Id, "1.0"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _client.PostAsync($"feeds/{Feed}/vsix/upload", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static IEnumerable<object[]> InvalidUploads()
    {
        yield return [new byte[] { 1, 2, 3 }, "not a valid VSIX (zip) file"];
        yield return [VsixPackageBuilder.Create(Id, "1.0", contentTypes: false), "[Content_Types].xml"];
        yield return [VsixPackageBuilder.Create(Id, "1.0.0-beta"), "The Version 1.0.0-beta of the manifest is not valid"];
        yield return
        [
            VsixPackageBuilder.Create("python", "1.0", VsixPackageBuilder.Manifest("python", "1.0", installation: "<InstallationTarget Id=\"Microsoft.VisualStudio.Code\" />")),
            "python is a VS Code extension",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidUploads))]
    public async Task InvalidUploadIsRejectedWithAMessage(byte[] vsix, string message)
    {
        await _app.CreateVsixFeedAsync(Feed);

        using var response = await _client.UploadVsixAsync(Feed, Id, "1.0", vsix);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesUploads()
    {
        await _app.CreateVsixFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.UploadVsixAsync(Feed, Id, "1.0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("read-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExtensionLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateVsixFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random bytes don't compress, so the file stays above 1 MiB.
        var vsix = VsixPackageBuilder.Create(Id, "1.0", extraFiles: new Dictionary<string, byte[]> { ["large.bin"] = RandomNumberGenerator.GetBytes(1024 * 1024) });
        using var response = await _client.UploadVsixAsync(Feed, Id, "1.0", vsix);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToUpload()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateVsixFeedAsync(Feed);

        using var anonymous = await client.UploadVsixAsync(Feed, Id, "1.0");

        client.UseBasicCredentials("ci", "wrong-key");
        using var wrong = await client.UploadVsixAsync(Feed, Id, "1.0");

        client.UseBasicCredentials("ci", "secret-key");
        using var right = await client.UploadVsixAsync(Feed, Id, "1.0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic realm=\"PaGetto\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Fact]
    public async Task ImagesTheExtensionDoesNotHaveAreNotLinked()
    {
        await _app.CreateVsixFeedAsync(Feed);
        var manifest = VsixPackageBuilder.Manifest(Id, "1.0").Replace(@"<PreviewImage>Resources\Preview.png</PreviewImage>", "");
        using (await _client.UploadVsixAsync(Feed, Id, "1.0", VsixPackageBuilder.Create(Id, "1.0", manifest)))
        {
        }

        var entry = (await _client.GetVsixGalleryAsync(Feed))[Id];
        using var preview = await _client.GetAsync($"feeds/{Feed}/vsix/extensions/{Id}/1.0/preview");

        Assert.NotNull(entry.IconUrl);
        Assert.Null(entry.PreviewImageUrl);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
    }

    [Theory]
    [InlineData("vsix/extensions/Contoso.HelloTools/2.0/Contoso.HelloTools-2.0.vsix")]
    [InlineData("vsix/extensions/Unknown/1.0/Unknown-1.0.vsix")]
    [InlineData("vsix/extensions/Contoso.HelloTools/1.0/Contoso.HelloTools-1.0.zip")]
    [InlineData("vsix/extensions/Contoso.HelloTools/1.0.0-beta/Contoso.HelloTools-1.0.0-beta.vsix")]
    [InlineData("vsix/extensions/Not%20Valid/1.0/x.vsix")]
    [InlineData("vsix/extensions/Unknown/1.0/icon")]
    [InlineData("vsix/extensions/Contoso.HelloTools/2.0/icon")]
    [InlineData("vsix/extensions/Contoso.HelloTools/1.0/other")]
    [InlineData("vsix/feed.xml")]
    [InlineData("vsix/")]
    public async Task UnknownExtensionsAndOtherPathsAreNotFound(string path)
    {
        await _app.CreateVsixFeedAsync(Feed);
        using (await _client.UploadVsixAsync(Feed, Id, "1.0"))
        {
        }

        using var response = await _client.GetAsync($"feeds/{Feed}/{path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "cran/src/contrib/PACKAGES")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnAVsixFeed(string method, string path)
    {
        await _app.CreateVsixFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "vsix/atom.xml")]
    [InlineData("GET", "vsix/extensions/Contoso.HelloTools/1.0/Contoso.HelloTools-1.0.vsix")]
    [InlineData("GET", "vsix/extensions/Contoso.HelloTools/1.0/icon")]
    [InlineData("PUT", "vsix/upload")]
    public async Task VsixApiIsNotFoundOnOtherFeeds(string method, string path)
    {
        await _app.CreateFeedAsync("nuget");
        await _app.CreateCranFeedAsync("r");

        foreach (var url in new[] { $"feeds/nuget/{path}", $"feeds/r/{path}", path })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsExtensions()
    {
        await _app.CreateVsixFeedAsync(Feed);
        using (await _client.UploadVsixAsync(Feed, Id, "1.2.0"))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("VSIX feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">VSIX</span>", index);
        Assert.Contains("Hello Tools", index);
        Assert.Contains($"http://localhost/feeds/{Feed}/vsix/atom.xml", index);

        var link = Regex.Match(index, $"href=\"(/feeds/{Feed}/artifacts/[^\"]+)\"").Groups[1].Value;
        Assert.Contains("<h1>Hello Tools</h1>", await _client.GetStringAsync(WebUtility.HtmlDecode(link)));

        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/contoso.hellotools");
        Assert.Contains("<h1>Hello Tools</h1>", details);
        Assert.Contains("Says hello from the editor.", details);
        Assert.Contains("Visual Studio [17.0, 18.0)", details);
        Assert.Contains(Id, details);
        Assert.Contains("Contoso.Shared", details);
        Assert.Contains("https://github.com/contoso/hello-tools", details);
        Assert.Contains("greeting", details);
        Assert.Contains($"/feeds/{Feed}/vsix/extensions/{Id}/1.2.0/{Id}-1.2.0.vsix", details);
        Assert.Contains($"VSIXInstaller.exe {Id}-1.2.0.vsix", details);
        Assert.Contains("Download VSIX", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/vsix/atom.xml", connect);
        Assert.Contains("Additional Extension Galleries", connect);
        Assert.Contains($"http://localhost/feeds/{Feed}/vsix/upload", connect);
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
