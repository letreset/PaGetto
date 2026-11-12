using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// A PyPI feed that mirrors another package index. The upstream is a PyPI feed of a second PaGetto
/// instance, so the mirror client is tested against a real simple API; tests of what such a feed
/// doesn't serve (the HTML form only, a wrong hash) use a fixed upstream instead.
/// </summary>
public class PyPIMirrorIntegrationTests : IDisposable
{
    private const string UpstreamFeed = "pypi";
    private const string MirrorFeed = "proxy";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _upstreamClient;
    private readonly HttpClient _downstreamClient;

    public PyPIMirrorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _upstreamClient = _upstream.CreateClient();
        _downstreamClient = _downstream.CreateClient();
    }

    [Fact]
    public async Task TheProjectListsTheFilesOfTheMirrorAndTheFeed()
    {
        await SetUpAsync();
        var upstreamWheel = await _upstreamClient.PublishPythonAsync(UpstreamFeed, "my-package", "1.0");
        await _upstreamClient.PublishPythonAsync(UpstreamFeed, "my-package", "2.0");
        await _downstreamClient.PublishPythonAsync(MirrorFeed, "my-package", "1.5");

        using var page = await _downstreamClient.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(MirrorFeed, "my-package"));
        var files = page.RootElement.GetProperty("files").EnumerateArray().ToList();

        Assert.Equal(new[] { "1.0", "1.5", "2.0" }, page.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(6, files.Count);

        // Files of the mirror are served by the feed, with the mirror's hashes.
        var mirrored = files[0];
        Assert.Equal(PyPITestClient.WheelFileName("my-package", "1.0"), mirrored.GetProperty("filename").GetString());
        Assert.Equal($"http://localhost/feeds/{MirrorFeed}/pypi/files/my-package/1.0/my_package-1.0-py3-none-any.whl", mirrored.GetProperty("url").GetString());
        Assert.Equal(PyPITestClient.Sha256(upstreamWheel), mirrored.GetProperty("hashes").GetProperty("sha256").GetString());
        Assert.Equal(">=3.9", mirrored.GetProperty("requires-python").GetString());

        // Listing doesn't store anything.
        Assert.Equal(new[] { "1.5" }, (await GetStoredVersionsAsync()).Select(v => v.Version));
    }

    [Fact]
    public async Task StoresAFileOnItsFirstDownload()
    {
        await SetUpAsync();
        var wheel = await _upstreamClient.PublishPythonAsync(UpstreamFeed, "my-package", "1.0", summary: "From upstream.");
        var fileName = PyPITestClient.WheelFileName("my-package", "1.0");

        var downloaded = await _downstreamClient.GetByteArrayAsync(PyPITestClient.FileUrl(MirrorFeed, "my-package", "1.0", fileName));

        Assert.Equal(wheel, downloaded);

        var stored = Assert.Single(await GetStoredVersionsAsync());
        Assert.Equal("1.0", stored.Version);
        Assert.Equal($"http://localhost/feeds/{UpstreamFeed}/pypi/simple", stored.CachedFrom);
        Assert.Equal("From upstream.", stored.Description);
        Assert.Equal(1, stored.Downloads);
        Assert.Equal(new[] { fileName, fileName + ".metadata" }, stored.Files.Select(f => f.FileName).Order());

        // The stored wheel now has its metadata, and the sdist that was not downloaded is still listed.
        using var page = await _downstreamClient.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(MirrorFeed, "my-package"));
        var files = page.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(new[] { fileName, PyPITestClient.SdistFileName("my-package", "1.0") }, files.Select(f => f.GetProperty("filename").GetString()));
        Assert.Equal(JsonValueKind.Object, files[0].GetProperty("core-metadata").ValueKind);

        // The mirrored package now shows up in the web UI of the mirror.
        Assert.Contains("my-package", await _downstreamClient.GetStringAsync($"feeds/{MirrorFeed}/"));
    }

    [Fact]
    public async Task AVersionPublishedToTheFeedHidesTheMirror()
    {
        await SetUpAsync();
        await _upstreamClient.PublishPythonAsync(UpstreamFeed, "my-package", "1.0");
        using (var upload = await _downstreamClient.UploadPythonAsync(MirrorFeed, "my-package", "1.0", PyPITestClient.SdistFileName("my-package", "1.0"), PyPITestClient.Sdist("my-package", "1.0", summary: "Our own.")))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using var page = await _downstreamClient.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(MirrorFeed, "my-package"));
        using var wheel = await _downstreamClient.GetAsync(PyPITestClient.FileUrl(MirrorFeed, "my-package", "1.0", PyPITestClient.WheelFileName("my-package", "1.0")));

        Assert.Equal(new[] { PyPITestClient.SdistFileName("my-package", "1.0") }, page.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("filename").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, wheel.StatusCode);
    }

    [Fact]
    public async Task ReadsAMirrorThatOnlyServesHtml()
    {
        var wheel = PyPITestClient.Wheel("my-package", "1.0");
        var fileName = PyPITestClient.WheelFileName("my-package", "1.0");
        using var upstream = new FixedUpstreamHandler(new Dictionary<string, (string, byte[])>
        {
            ["/simple/my-package/"] = ("text/html", Encoding.UTF8.GetBytes(
                $"""
                <!DOCTYPE html>
                <html><body>
                <a href="../../packages/{fileName}#sha256={PyPITestClient.Sha256(wheel)}" data-requires-python="&gt;=3.9" data-yanked="broken">{fileName}</a>
                <a href="https://files.test/my_package-1.0-py2.7.egg">my_package-1.0-py2.7.egg</a>
                </body></html>
                """)),
            [$"/packages/{fileName}"] = ("application/octet-stream", wheel),
        });
        using var app = new PaGettoApplication(_output, upstream);
        using var client = app.CreateClient();
        await app.CreatePyPIFeedAsync(MirrorFeed, mirrorSource: "https://upstream.test/simple");

        using var page = await client.GetSimpleJsonAsync(PyPITestClient.ProjectUrl(MirrorFeed, "my-package"));
        var file = Assert.Single(page.RootElement.GetProperty("files").EnumerateArray());
        var downloaded = await client.GetByteArrayAsync(PyPITestClient.FileUrl(MirrorFeed, "my-package", "1.0", fileName));

        Assert.Equal(">=3.9", file.GetProperty("requires-python").GetString());
        Assert.Equal("broken", file.GetProperty("yanked").GetString());
        Assert.Equal(wheel, downloaded);

        // A yanked file of the mirror is stored as a yanked (unlisted) version.
        Assert.False(Assert.Single(await GetStoredVersionsAsync(app)).Listed);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchTheMirrorsHashIsNotStored()
    {
        var fileName = PyPITestClient.WheelFileName("my-package", "1.0");
        using var upstream = new FixedUpstreamHandler(new Dictionary<string, (string, byte[])>
        {
            ["/simple/my-package/"] = ("application/vnd.pypi.simple.v1+json", Encoding.UTF8.GetBytes(
                $$"""{ "meta": { "api-version": "1.1" }, "name": "my-package", "files": [{ "filename": "{{fileName}}", "url": "/packages/{{fileName}}", "hashes": { "sha256": "{{new string('0', 64)}}" } }] }""")),
            [$"/packages/{fileName}"] = ("application/octet-stream", PyPITestClient.Wheel("my-package", "1.0")),
        });
        using var app = new PaGettoApplication(_output, upstream);
        using var client = app.CreateClient();
        await app.CreatePyPIFeedAsync(MirrorFeed, mirrorSource: "https://upstream.test/simple");

        using var response = await client.GetAsync(PyPITestClient.FileUrl(MirrorFeed, "my-package", "1.0", fileName));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredVersionsAsync(app));
    }

    [Fact]
    public async Task UnknownProjectIsNotFound()
    {
        await SetUpAsync();

        using var page = await _downstreamClient.GetAsync(PyPITestClient.ProjectUrl(MirrorFeed, "unknown"));
        using var file = await _downstreamClient.GetAsync(PyPITestClient.FileUrl(MirrorFeed, "unknown", "1.0", "unknown-1.0.tar.gz"));

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    private async Task SetUpAsync()
    {
        await _upstream.CreatePyPIFeedAsync(UpstreamFeed);
        await _downstream.CreatePyPIFeedAsync(MirrorFeed, mirrorSource: $"http://localhost/feeds/{UpstreamFeed}/pypi/simple");
    }

    private Task<ArtifactVersion[]> GetStoredVersionsAsync()
    {
        return GetStoredVersionsAsync(_downstream);
    }

    private static async Task<ArtifactVersion[]> GetStoredVersionsAsync(PaGettoApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions.AsNoTracking().Include(v => v.Files).ToArrayAsync();
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
        _downstreamClient.Dispose();
        _upstream.Dispose();
        _downstream.Dispose();
    }

    /// <summary>
    /// An upstream that serves fixed content by path, and 404 for anything else.
    /// </summary>
    private sealed class FixedUpstreamHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, (string ContentType, byte[] Content)> _files;

        public FixedUpstreamHandler(IReadOnlyDictionary<string, (string ContentType, byte[] Content)> files)
        {
            _files = files;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_files.TryGetValue(request.RequestUri!.AbsolutePath, out var file))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
