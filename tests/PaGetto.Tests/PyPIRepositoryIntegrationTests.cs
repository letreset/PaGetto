using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.PyPI;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The package index of a PyPI feed, in Legacy mode without credentials (open access). The requests
/// are the ones pip, uv and twine send; see also the check with the real clients in the docs.
/// </summary>
public class PyPIRepositoryIntegrationTests : IDisposable
{
    private const string Feed = "python";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public PyPIRepositoryIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task UploadedFilesAreListedInTheJsonForm()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        var wheel = await _client.PublishPythonAsync(Feed, "My_Package", "1.0");
        await _client.PublishPythonAsync(Feed, "my-package", "2.0rc1");

        using var page = await _client.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(Feed, "my-package"));
        var root = page.RootElement;

        Assert.Equal("1.1", root.GetProperty("meta").GetProperty("api-version").GetString());
        Assert.Equal("my-package", root.GetProperty("name").GetString());
        Assert.Equal(new[] { "1.0", "2.0rc1" }, root.GetProperty("versions").EnumerateArray().Select(v => v.GetString()));

        var files = root.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "my_package-1.0-py3-none-any.whl", "my_package-1.0.tar.gz", "my_package-2.0rc1-py3-none-any.whl", "my_package-2.0rc1.tar.gz" },
            files.Select(f => f.GetProperty("filename").GetString()));

        var first = files[0];
        Assert.Equal($"http://localhost/feeds/{Feed}/pypi/files/my-package/1.0/my_package-1.0-py3-none-any.whl", first.GetProperty("url").GetString());
        Assert.Equal(PyPITestClient.Sha256(wheel), first.GetProperty("hashes").GetProperty("sha256").GetString());
        Assert.Equal(">=3.9", first.GetProperty("requires-python").GetString());
        Assert.False(first.GetProperty("yanked").GetBoolean());
        Assert.Equal(wheel.Length, first.GetProperty("size").GetInt64());
        Assert.Matches(@"^2020-01-01T\d{2}:00:00\.000000Z$", first.GetProperty("upload-time").GetString());

        // The metadata of a wheel is served next to it (PEP 658); an sdist has none.
        var metadataHash = first.GetProperty("core-metadata").GetProperty("sha256").GetString();
        Assert.Equal(metadataHash, first.GetProperty("dist-info-metadata").GetProperty("sha256").GetString());
        Assert.False(files[1].GetProperty("core-metadata").GetBoolean());

        var metadata = await _client.GetByteArrayAsync(first.GetProperty("url").GetString() + ".metadata");
        Assert.Equal(PyPITestClient.Sha256(metadata), metadataHash);
        Assert.Equal(PyPITestClient.Metadata("My_Package", "1.0"), Encoding.UTF8.GetString(metadata));
    }

    [Fact]
    public async Task UploadedFilesAreListedInTheHtmlForm()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        var wheel = await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        using var response = await _client.GetAsync(PyPITestClient.ProjectUrl(Feed, "my-package"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<meta name=\"pypi:repository-version\" content=\"1.1\">", html);
        Assert.Contains(
            $"<a href=\"http://localhost/feeds/{Feed}/pypi/files/my-package/1.0/my_package-1.0-py3-none-any.whl#sha256={PyPITestClient.Sha256(wheel)}\" data-requires-python=\"&gt;=3.9\" data-core-metadata=\"sha256=",
            html);
        Assert.Contains(">my_package-1.0.tar.gz</a>", html);
    }

    [Theory]
    // What pip sends.
    [InlineData("application/vnd.pypi.simple.v1+json, application/vnd.pypi.simple.v1+html; q=0.1, text/html; q=0.01", "application/vnd.pypi.simple.v1+json")]
    [InlineData("application/vnd.pypi.simple.v1+html", "application/vnd.pypi.simple.v1+html")]
    [InlineData("text/html, application/vnd.pypi.simple.v1+json; q=0.5", "text/html")]
    [InlineData("application/vnd.pypi.simple.latest+json", "application/vnd.pypi.simple.v1+json")]
    [InlineData("*/*", "text/html")]
    public async Task TheFormFollowsTheAcceptHeader(string accept, string contentType)
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, PyPITestClient.ProjectUrl(Feed, "my-package"));
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await _client.SendAsync(request);

        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept", response.Headers.Vary);
    }

    [Fact]
    public async Task TheRootListsTheProjects()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "Zeta", "1.0");
        await _client.PublishPythonAsync(Feed, "alpha.beta", "1.0");

        using var json = await _client.GetSimpleJsonAsync(PyPITestClient.SimpleUrl(Feed));
        var html = await _client.GetStringAsync(PyPITestClient.SimpleUrl(Feed));

        Assert.Equal(new[] { "alpha-beta", "zeta" }, json.RootElement.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Contains($"<a href=\"http://localhost/feeds/{Feed}/pypi/simple/alpha-beta/\">alpha-beta</a>", html);
    }

    [Fact]
    public async Task ANameThatIsNotNormalizedRedirectsToItsNormalizedName()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        using var response = await _client.GetAsync(PyPITestClient.ProjectUrl(Feed, "My_Package"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"http://localhost/feeds/{Feed}/pypi/simple/my-package/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task FilesAreDownloadedAndCounted()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        var wheel = await _client.PublishPythonAsync(Feed, "my-package", "1.0");
        var url = PyPITestClient.FileUrl(Feed, "my-package", "1.0", PyPITestClient.WheelFileName("my-package", "1.0"));

        using var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        var download = await _client.GetByteArrayAsync(url);
        await _client.GetByteArrayAsync(url + ".metadata");

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(wheel, download);

        // Neither HEAD nor the metadata counts.
        var version = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal(1, version.Downloads);
    }

    [Theory]
    [InlineData("my-package", "1.0", "my_package-2.0.tar.gz")]
    [InlineData("my-package", "2.0", "my_package-2.0.tar.gz")]
    [InlineData("my-package", "1.0", "my_package-1.0.tar.gz.metadata")]
    [InlineData("other", "1.0", "other-1.0.tar.gz")]
    public async Task UnknownFilesAreNotFound(string name, string version, string fileName)
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        using var response = await _client.GetAsync(PyPITestClient.FileUrl(Feed, name, version, fileName));
        using var project = await _client.GetAsync(PyPITestClient.ProjectUrl(Feed, "other"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, project.StatusCode);
    }

    [Fact]
    public async Task TheVersionKeepsTheMetadataAndReadmeOfItsFirstFile()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "My_Package", "1.0", summary: "The summary.");

        var version = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("my-package", version.Artifact.Name);
        Assert.Equal("1.0", version.Version);
        Assert.Equal("The summary.", version.Description);
        Assert.Equal(">=3.9", PyPIManifestSummary.Parse(version.MetadataJson).RequiresPython);
        Assert.True(version.HasReadme);
        Assert.Null(version.CachedFrom);
    }

    [Fact]
    public async Task UploadingTheSameFileAgainSucceeds()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        Assert.Single(await GetStoredVersionsAsync());
    }

    [Theory]
    [InlineData(PackageOverwriteAllowed.False, "1.0", HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0", HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0b1", HttpStatusCode.OK)]
    [InlineData(PackageOverwriteAllowed.True, "1.0", HttpStatusCode.OK)]
    public async Task OtherContentForAFileFollowsTheOverwritePolicy(PackageOverwriteAllowed policy, string version, HttpStatusCode expected)
    {
        await _app.CreatePyPIFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = policy);
        await _client.PublishPythonAsync(Feed, "my-package", version);

        var fileName = PyPITestClient.WheelFileName("my-package", version);
        var changed = PyPITestClient.Wheel("my-package", version, marker: "changed");
        using var response = await _client.UploadPythonAsync(Feed, "my-package", version, fileName, changed);

        Assert.Equal(expected, response.StatusCode);
        var stored = await _client.GetByteArrayAsync(PyPITestClient.FileUrl(Feed, "my-package", version, fileName));
        if (expected == HttpStatusCode.Conflict)
        {
            // twine --skip-existing looks for this reason.
            Assert.StartsWith("File already exists", response.ReasonPhrase);
            Assert.NotEqual(changed, stored);
        }
        else
        {
            Assert.Equal(changed, stored);
        }
    }

    [Theory]
    [InlineData("my package", "1.0", "my_package-1.0.tar.gz", "is not a valid project name")]
    [InlineData("my-package", "1.0", "other-1.0.tar.gz", "is not the name of a wheel or an sdist")]
    [InlineData("my-package", "1.0", "my_package-1.0-py2.7.egg", "is not the name of a wheel or an sdist")]
    [InlineData("my-package", "2.0", "my_package-1.0.tar.gz", "uploaded for version 2.0")]
    public async Task UploadsThatDoNotMatchTheirFileAreRejected(string name, string version, string fileName, string message)
    {
        await _app.CreatePyPIFeedAsync(Feed);

        using var response = await _client.UploadPythonAsync(Feed, name, version, fileName, PyPITestClient.Sdist("my-package", "1.0"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task AFileWhoseMetadataIsForAnotherVersionIsRejected()
    {
        await _app.CreatePyPIFeedAsync(Feed);

        using var response = await _client.UploadPythonAsync(Feed, "my-package", "2.0", PyPITestClient.WheelFileName("my-package", "2.0"), PyPITestClient.Wheel("my-package", "1.0"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("is for version '1.0'", response.ReasonPhrase);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchItsDigestIsRejected()
    {
        await _app.CreatePyPIFeedAsync(Feed);

        using var response = await _client.UploadPythonAsync(Feed, "my-package", "1.0", PyPITestClient.SdistFileName("my-package", "1.0"), PyPITestClient.Sdist("my-package", "1.0"), sha256Digest: new string('0', 64));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("digest", response.ReasonPhrase);
    }

    [Fact]
    public async Task OnlyFileUploadsAreAccepted()
    {
        await _app.CreatePyPIFeedAsync(Feed);

        using var form = new MultipartFormDataContent { { new StringContent("submit"), ":action" } };
        using var response = await _client.PostAsync(PyPITestClient.UploadUrl(Feed), form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyFeedRejectsUploads()
    {
        await _app.CreatePyPIFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var response = await _client.UploadPythonAsync(Feed, "my-package", "1.0", PyPITestClient.SdistFileName("my-package", "1.0"), PyPITestClient.Sdist("my-package", "1.0"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("This feed is read-only.", response.ReasonPhrase);
    }

    [Fact]
    public async Task AFileLargerThanTheFeedAllowsIsRejected()
    {
        await _app.CreatePyPIFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);
        var large = new byte[(1024 * 1024) + 1];

        using var response = await _client.UploadPythonAsync(Feed, "my-package", "1.0", PyPITestClient.SdistFileName("my-package", "1.0"), large);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync());
    }

    [Fact]
    public async Task AnUnlistedVersionIsYanked()
    {
        var feed = await _app.CreatePyPIFeedAsync(Feed);
        await _client.PublishPythonAsync(Feed, "my-package", "1.0");

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IArtifactDatabase>().SetListedAsync(feed.Id, "my-package", "1.0", listed: false, default);
        }

        using var json = await _client.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(Feed, "my-package"));
        var html = await _client.GetStringAsync(PyPITestClient.ProjectUrl(Feed, "my-package"));

        Assert.All(json.RootElement.GetProperty("files").EnumerateArray(), f => Assert.True(f.GetProperty("yanked").GetBoolean()));
        Assert.Contains("data-yanked=\"\"", html);
    }

    [Fact]
    public async Task OtherFeedTypesDoNotAnswer()
    {
        await _app.CreatePyPIFeedAsync(Feed);
        await _app.CreateMavenFeedAsync("maven");

        using var pypiOnMaven = await _client.GetAsync(PyPITestClient.SimpleUrl("maven"));
        using var nugetOnPyPI = await _client.GetAsync($"feeds/{Feed}/v3/index.json");
        using var pypiOnDefault = await _client.GetAsync("pypi/simple/");

        Assert.Equal(HttpStatusCode.NotFound, pypiOnMaven.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetOnPyPI.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pypiOnDefault.StatusCode);
    }

    private async Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().Include(v => v.Artifact).Include(v => v.Files).ToArrayAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
