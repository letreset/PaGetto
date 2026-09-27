using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Covers the ReadmeUriTemplate service index resource, which Visual Studio uses to show a package's readme,
/// and the readme endpoint it points at.
/// See: https://learn.microsoft.com/nuget/api/readme-template-resource
/// </summary>
public class ReadmeIntegrationTests : IDisposable
{
    private const string ReadmeResourceType = "ReadmeUriTemplate/6.13.0";
    private const string PackageId = "Readme.Test";
    private const string PackageVersion = "1.0.0-Beta";
    private const string ReadmeContent = "# Readme.Test";
    private const string NamedFeedSlug = "team";

    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;
    private readonly string _readmePath;

    public ReadmeIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();

        // PackageBuilder reads each file more than once, so the readme has to live on disk.
        _readmePath = Path.GetTempFileName();
        File.WriteAllText(_readmePath, ReadmeContent);
    }

    [Fact]
    public async Task RootServiceIndexAdvertisesReadmeUriTemplate()
    {
        var template = await GetReadmeTemplateAsync("v3/index.json");

        Assert.Equal("http://localhost/v3/package/{lower_id}/{lower_version}/readme", template);
    }

    [Fact]
    public async Task NamedFeedServiceIndexAdvertisesFeedScopedReadmeUriTemplate()
    {
        await _app.CreateFeedAsync(NamedFeedSlug);

        var template = await GetReadmeTemplateAsync($"feeds/{NamedFeedSlug}/v3/index.json");

        Assert.Equal($"http://localhost/feeds/{NamedFeedSlug}/v3/package/{{lower_id}}/{{lower_version}}/readme", template);
    }

    [Fact]
    public async Task ReadmeTemplateResolvesToPackageReadme()
    {
        await AddPackageWithReadmeAsync(stream => _app.AddPackageAsync(stream));

        var template = await GetReadmeTemplateAsync("v3/index.json");
        using var response = await _client.GetAsync(Expand(template, PackageId, PackageVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ReadmeContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NamedFeedReadmeTemplateResolvesToPackageReadme()
    {
        await _app.CreateFeedAsync(NamedFeedSlug);
        await AddPackageWithReadmeAsync(stream => _app.AddPackageToFeedAsync(stream, NamedFeedSlug));

        var template = await GetReadmeTemplateAsync($"feeds/{NamedFeedSlug}/v3/index.json");
        using var response = await _client.GetAsync(Expand(template, PackageId, PackageVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReadmeContent, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadmeReturnsNotFoundForPackageWithoutReadme()
    {
        using var packageStream = TestResources.GetResourceStream(TestResources.Package);
        await _app.AddPackageAsync(packageStream);

        using var response = await _client.GetAsync("v3/package/testdata/1.2.3/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadmeReturnsNotFoundForUnknownPackage()
    {
        using var response = await _client.GetAsync("v3/package/packagedoesnotexist/1.0.0/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadmeReturnsNotFoundForInvalidVersion()
    {
        using var response = await _client.GetAsync("v3/package/testdata/not-a-version/readme");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        File.Delete(_readmePath);
    }

    private static string Expand(string template, string id, string version)
    {
        return template
            .Replace("{lower_id}", id.ToLowerInvariant())
            .Replace("{lower_version}", NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant());
    }

    private async Task<string> GetReadmeTemplateAsync(string serviceIndexUrl)
    {
        using var response = await _client.GetAsync(serviceIndexUrl);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = doc.RootElement
            .GetProperty("resources")
            .EnumerateArray()
            .Single(r => r.GetProperty("@type").GetString() == ReadmeResourceType);

        return resource.GetProperty("@id").GetString();
    }

    private async Task AddPackageWithReadmeAsync(Func<Stream, Task> addPackageAsync)
    {
        var builder = new PackageBuilder
        {
            Id = PackageId,
            Version = NuGetVersion.Parse(PackageVersion),
            Description = "Test description",
            Readme = "README.md",
        };
        builder.Authors.Add("Test author");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = _readmePath,
            TargetPath = "README.md",
        });

        using var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;

        await addPackageAsync(stream);
    }
}
