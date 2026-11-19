using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.VsCode;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The gallery of a VS Code feed, in Legacy mode without credentials (open access).
/// </summary>
public class VsCodeGalleryIntegrationTests : IDisposable
{
    private const string Feed = "code";
    private const string Id = "Contoso.hello-world";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public VsCodeGalleryIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task UploadedExtensionIsFoundAndInstalled()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        var vsix = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0");

        using var upload = await _client.UploadVsCodeAsync(Feed, vsix);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        // VS Code finds an extension by its identifier, regardless of case.
        var extension = await _client.FindVsCodeExtensionAsync(Feed, "contoso.HELLO-world");
        Assert.NotNull(extension);
        Assert.Equal("hello-world", extension.ExtensionName);
        Assert.Equal("Hello World", extension.DisplayName);
        Assert.Equal("Says hello from the editor.", extension.ShortDescription);
        Assert.Equal("Contoso", extension.Publisher.PublisherName);
        Assert.Equal(VsCodeExtensionId.GetUuid("extension", Id), extension.ExtensionId);
        Assert.Equal(DateTimeKind.Utc, extension.LastUpdated.Kind);
        Assert.Equal("hello-world", Assert.Single((await _client.QueryVsCodeAsync(Feed, [(VsCodeGalleryApi.ExtensionIdFilter, extension.ExtensionId.ToString())])).Extensions).ExtensionName);
        Assert.Equal(["Other", "Snippets"], extension.Categories);

        var version = Assert.Single(extension.Versions);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(VsCodeTargetPlatform.Universal, version.TargetPlatform);
        Assert.Equal($"http://localhost/feeds/{Feed}/vscode/asset/Contoso/hello-world/1.0.0", version.AssetUri);
        Assert.Equal(version.AssetUri, version.FallbackAssetUri);
        Assert.Contains(version.Properties, p => p.Key == VsCodeGalleryApi.EngineProperty && p.Value == "^1.90.0");

        // Marked private, so VS Code installs it without a signature.
        Assert.Contains(version.Properties, p => p.Key == VsCodeGalleryApi.PrivateProperty && p.Value == "true");

        // VS Code reads the manifest, then downloads the VSIX file from the fallback asset URI.
        var manifest = await _client.GetStringAsync(version.GetAssetUrl(VsCodeGalleryApi.ManifestAsset));
        Assert.Contains("\"engines\"", manifest);
        Assert.Equal(VsCodePackageBuilder.Readme, await _client.GetStringAsync(version.GetAssetUrl(VsCodeGalleryApi.DetailsAsset)));
        Assert.Equal(VsCodePackageBuilder.Icon, await _client.GetByteArrayAsync(version.GetAssetUrl(VsCodeGalleryApi.IconAsset)));
        Assert.Contains("PackageManifest", await _client.GetStringAsync(version.GetAssetUrl(VsCodeGalleryApi.VsixManifestAsset)));

        using var download = await _client.GetAsync($"{version.FallbackAssetUri}/{VsCodeGalleryApi.VsixPackageAsset}?redirect=true&targetPlatform=universal");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(vsix, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal($"{Id}-1.0.0.vsix", download.Content.Headers.ContentDisposition!.FileName);

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("contoso.hello-world", stored.Artifact.Name);
        Assert.Equal("Says hello from the editor.", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.Equal(1, stored.Downloads);

        var install = Assert.Single((await _client.FindVsCodeExtensionAsync(Feed, Id)).Statistics, s => s.StatisticName == "install");
        Assert.Equal(1, install.Value);
    }

    [Fact]
    public async Task LatestHasTheLatestStableAndPreReleaseVersion()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await UploadAsync("1.0.0");
        await UploadAsync("1.1.0");
        await UploadAsync("1.2.0", VsCodePackageBuilder.Create(
            "Contoso",
            "hello-world",
            "1.2.0",
            manifest: VsCodePackageBuilder.Manifest("Contoso", "hello-world", "1.2.0", extraProperties: """<Property Id="Microsoft.VisualStudio.Code.PreRelease" Value="true" />""")));

        using var response = await _client.GetAsync($"feeds/{Feed}/vscode/gallery/vscode/contoso/hello-world/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var latest = await response.Content.ReadFromJsonAsync<VsCodeGalleryExtension>();

        Assert.Equal(["1.2.0", "1.1.0"], latest!.Versions.Select(v => v.Version));

        // A query for all versions has all of them, newest first.
        var all = await _client.FindVsCodeExtensionAsync(Feed, Id);
        Assert.Equal(["1.2.0", "1.1.0", "1.0.0"], all.Versions.Select(v => v.Version));
        Assert.True((await GetStoredVersionAsync("1.2.0")).IsPrerelease);

        // VS Code asks for the latest version only when it lists extensions.
        var listed = await _client.QueryVsCodeAsync(Feed, [(VsCodeGalleryApi.ExtensionNameFilter, Id)], flags: VsCodeGalleryApi.IncludeLatestVersionOnlyFlag);
        Assert.Equal("1.2.0", Assert.Single(Assert.Single(listed.Extensions).Versions).Version);
    }

    [Fact]
    public async Task VersionHasAFileForEachPlatform()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        var linux = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0", "linux-x64");
        var windows = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0", "win32-x64");
        await UploadAsync("1.0.0", linux);
        await UploadAsync("1.0.0", windows);

        var extension = await _client.FindVsCodeExtensionAsync(Feed, Id);

        Assert.Equal(["linux-x64", "win32-x64"], extension.Versions.Select(v => v.TargetPlatform));
        Assert.Equal(linux, await _client.GetByteArrayAsync(extension.Versions[0].GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        Assert.Equal(windows, await _client.GetByteArrayAsync(extension.Versions[1].GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));

        // One version with a file per platform.
        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal(
            ["Contoso.hello-world-1.0.0@linux-x64.vsix", "Contoso.hello-world-1.0.0@win32-x64.vsix"],
            stored.Files.Select(f => f.FileName).Order(StringComparer.Ordinal));
        Assert.Equal(linux.Length + windows.Length, stored.Size);

        // A platform the version has no file for is not found.
        using var missing = await _client.GetAsync($"feeds/{Feed}/vscode/asset/Contoso/hello-world/1.0.0/{VsCodeGalleryApi.VsixPackageAsset}?targetPlatform=darwin-arm64");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SearchFindsExtensionsByTextAndCategory()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await UploadAsync("1.0.0");
        await UploadAsync("2.0.0", VsCodePackageBuilder.Create("Fabrikam", "linter", "2.0.0"));

        var all = await _client.QueryVsCodeAsync(Feed, [], flags: VsCodeGalleryApi.IncludeLatestVersionOnlyFlag);
        var text = await _client.QueryVsCodeAsync(Feed, [(VsCodeGalleryApi.SearchTextFilter, "lint")]);
        var category = await _client.QueryVsCodeAsync(Feed, [(VsCodeGalleryApi.CategoryFilter, "snippets")]);
        var none = await _client.QueryVsCodeAsync(Feed, [(VsCodeGalleryApi.TagFilter, "missing")]);
        var paged = await _client.QueryVsCodeAsync(Feed, [], pageNumber: 2, pageSize: 1);

        Assert.Equal(2, all.GetTotalCount());
        Assert.Equal(["hello-world", "linter"], all.Extensions.Select(e => e.ExtensionName).Order(StringComparer.Ordinal));
        Assert.Equal("linter", Assert.Single(text.Extensions).ExtensionName);
        Assert.Equal(2, category.GetTotalCount());
        Assert.Empty(none.Extensions);
        Assert.Equal(2, paged.GetTotalCount());
        Assert.Single(paged.Extensions);
    }

    [Fact]
    public async Task UnlistedVersionIsNotOffered()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await UploadAsync("1.0.0");
        await UploadAsync("1.1.0");

        await SetListedAsync("1.1.0", listed: false);

        Assert.Equal(["1.0.0"], (await _client.FindVsCodeExtensionAsync(Feed, Id)).Versions.Select(v => v.Version));
    }

    [Fact]
    public async Task ExistingVersionIsNotReplaced()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await UploadAsync("1.0.0");

        using var again = await _client.UploadVsCodeAsync(Feed, VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already exists", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VisualStudioExtensionIsRefused()
    {
        await _app.CreateVsCodeFeedAsync(Feed);

        using var upload = await _client.UploadVsCodeAsync(Feed, VsixPackageBuilder.Create("Contoso.HelloTools", "1.0"));

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("does not target Microsoft.VisualStudio.Code", await upload.Content.ReadAsStringAsync());
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task VsixFeedRefusesVsCodeExtensions()
    {
        await _app.CreateVsixFeedAsync("vs");

        using var upload = await _client.UploadVsixAsync("vs", "hello-world", "1.0.0", VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0"));

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("VS Code extension", await upload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownExtensionIsNotFound()
    {
        await _app.CreateVsCodeFeedAsync(Feed);

        using var latest = await _client.GetAsync($"feeds/{Feed}/vscode/gallery/vscode/contoso/missing/latest");
        using var asset = await _client.GetAsync($"feeds/{Feed}/vscode/asset/contoso/missing/1.0.0/{VsCodeGalleryApi.VsixPackageAsset}");

        Assert.Equal(HttpStatusCode.NotFound, latest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asset.StatusCode);
        Assert.Null(await _client.FindVsCodeExtensionAsync(Feed, "contoso.missing"));
    }

    [Fact]
    public async Task GalleryIsNotFoundOnFeedsOfOtherTypes()
    {
        await _app.CreateVsixFeedAsync("vs");

        using var query = await _client.PostVsCodeQueryAsync("feeds/vs/vscode/gallery", []);
        using var upload = await _client.UploadVsCodeAsync("vs", VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0"));

        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
    }

    [Fact]
    public async Task ItemUrlOpensThePackagePage()
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await UploadAsync("1.0.0");

        using var item = await _client.GetAsync($"feeds/{Feed}/vscode/item?itemName={Id}");
        Assert.Equal(HttpStatusCode.Redirect, item.StatusCode);
        Assert.Equal($"/feeds/{Feed}/artifacts/contoso.hello-world", item.Headers.Location!.OriginalString);

        var page = await _client.GetStringAsync(item.Headers.Location.OriginalString);
        Assert.Contains("Hello World", page);
        Assert.Contains($"code --install-extension {Id}@1.0.0", page);
        Assert.Contains("^1.90.0", page);
        Assert.Contains("https://github.com/contoso/hello-world.git", page);
        Assert.DoesNotContain("__ext_hello", page);

        var list = await _client.GetStringAsync($"feeds/{Feed}");
        Assert.Contains("Hello World", list);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/vscode/gallery", connect);
        Assert.Contains("extensionsGallery", connect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromotesAVersionWithTheFileOfEachPlatform(bool move)
    {
        await _app.CreateVsCodeFeedAsync(Feed);
        await _app.CreateVsCodeFeedAsync("release");
        var linux = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0", "linux-x64");
        var windows = VsCodePackageBuilder.Create("Contoso", "hello-world", "1.0.0", "win32-x64");
        await UploadAsync("1.0.0", linux);
        await UploadAsync("1.0.0", windows);

        using var promote = await _client.PostAsJsonAsync(
            $"feeds/{Feed}/api/v1/promote",
            new { packageId = "contoso.hello-world", version = "1.0.0", targetFeed = "release", move });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        var extension = await _client.FindVsCodeExtensionAsync("release", Id);
        Assert.Equal(["linux-x64", "win32-x64"], extension.Versions.Select(v => v.TargetPlatform));
        Assert.All(extension.Versions, v => Assert.Contains(v.Properties, p => p.Key == VsCodeGalleryApi.PrivateProperty));
        Assert.Equal(linux, await _client.GetByteArrayAsync(extension.Versions[0].GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        Assert.Equal(windows, await _client.GetByteArrayAsync(extension.Versions[1].GetAssetUrl(VsCodeGalleryApi.VsixPackageAsset)));
        Assert.Equal(VsCodePackageBuilder.Readme, await _client.GetStringAsync(extension.Versions[0].GetAssetUrl(VsCodeGalleryApi.DetailsAsset)));
        Assert.Contains("Says hello from the editor.", await _client.GetStringAsync("feeds/release/artifacts/contoso.hello-world"));

        // A move leaves nothing in the source feed; a copy leaves it as it was.
        var source = await _client.FindVsCodeExtensionAsync(Feed, Id);
        if (move)
        {
            Assert.Null(source);
        }
        else
        {
            Assert.Equal(2, source.Versions.Count);
        }
    }

    private async Task UploadAsync(string version, byte[] vsix = null)
    {
        using var upload = await _client.UploadVsCodeAsync(Feed, vsix ?? VsCodePackageBuilder.Create("Contoso", "hello-world", version));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .Include(v => v.Files)
            .ToArrayAsync();
    }

    private async Task<ArtifactVersion> GetStoredVersionAsync(string version)
    {
        return Assert.Single(await GetStoredVersionsAsync(), v => v.Version == version);
    }

    private async Task SetListedAsync(string version, bool listed)
    {
        using var scope = _app.Services.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactDatabase>();
        var stored = await GetStoredVersionAsync(version);

        Assert.True(await artifacts.SetListedAsync(stored.Artifact.FeedId, stored.Artifact.Name, version, listed, default));
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
