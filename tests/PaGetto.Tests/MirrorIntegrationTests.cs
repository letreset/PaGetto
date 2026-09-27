using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class MirrorIntegrationTests : IDisposable
{
    private readonly PaGettoApplication _upstream;
    private readonly PaGettoApplication _downstream;
    private readonly HttpClient _downstreamClient;
    private readonly Stream _packageStream;
    private readonly ITestOutputHelper _output;

    public MirrorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _upstream = new PaGettoApplication(output);
        _downstream = new PaGettoApplication(output, _upstream.Server.CreateHandler());

        _downstreamClient = _downstream.CreateClient();
        _packageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Fact]
    public async Task SearchExcludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        using var downstreamResponse = await _downstreamClient.GetAsync("v3/search");
        var downstreamContent = await downstreamResponse.Content.ReadAsStreamAsync();
        var downstreamJson = downstreamContent.ToPrettifiedJson();

        // The downstream package source should not have the package.
        Assert.Equal(HttpStatusCode.OK, downstreamResponse.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration/""
  },
  ""totalHits"": 0,
  ""data"": []
}", downstreamJson);
    }

    [Fact]
    public async Task VersionListIncludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        var response = await _downstreamClient.GetAsync("v3/package/TestData/index.json");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
    }

    [Fact]
    public async Task PackageDownloadIncludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        using var response = await _downstreamClient.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadIncludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        using var response = await _downstreamClient.GetAsync(
            "v3/package/TestData/1.2.3/TestData.1.2.3.nuspec");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataIncludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        using var response = await _downstreamClient.GetAsync("v3/registration/TestData/index.json");
        var content = await response.Content.ReadAsStreamAsync();
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/index.json"",
  ""@type"": [
    ""catalog:CatalogRoot"",
    ""PackageRegistration"",
    ""catalog:Permalink""
  ],
  ""count"": 1,
  ""items"": [
    {
      ""@id"": ""http://localhost/v3/registration/testdata/index.json"",
      ""count"": 1,
      ""lower"": ""1.2.3"",
      ""upper"": ""1.2.3"",
      ""items"": [
        {
          ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
          ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
          ""catalogEntry"": {
            ""downloads"": 0,
            ""hasReadme"": false,
            ""packageTypes"": [],
            ""repositoryUrl"": """",
            ""id"": ""TestData"",
            ""version"": ""1.2.3"",
            ""authors"": ""Test author"",
            ""dependencyGroups"": [
              {
                ""targetFramework"": ""net5.0"",
                ""dependencies"": []
              }
            ],
            ""description"": ""Test description"",
            ""iconUrl"": """",
            ""language"": """",
            ""licenseUrl"": """",
            ""listed"": true,
            ""minClientVersion"": """",
            ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
            ""projectUrl"": """",
            ""published"": ""2020-01-01T00:00:00Z"",
            ""requireLicenseAcceptance"": false,
            ""summary"": """",
            ""tags"": [],
            ""title"": """"
          }
        }
      ]
    }
  ],
  ""totalDownloads"": 0
}", json);
    }

    [Fact]
    public async Task PackageMetadataLeafIncludesUpstream()
    {
        await _upstream.AddPackageAsync(_packageStream);

        using var response = await _downstreamClient.GetAsync("v3/registration/TestData/1.2.3.json");
        var content = await response.Content.ReadAsStreamAsync();
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
  ""@type"": [
    ""Package"",
    ""http://schema.nuget.org/catalog#Permalink""
  ],
  ""listed"": true,
  ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
  ""published"": ""2020-01-01T00:00:00Z"",
  ""registration"": ""http://localhost/v3/registration/testdata/index.json""
}", json);
    }

    [Fact]
    public async Task SeveralUpstreamsServePackagesFromEach()
    {
        using var nugetOrg = new PaGettoApplication(_output);
        using var vendor = new PaGettoApplication(_output);
        await nugetOrg.AddPackageAsync(_packageStream);
        await vendor.AddPackageAsync(BuildPackage("Vendor.Package", "1.0.0"));

        var handler = new HostRoutingHandler()
            .Route("nuget.test", nugetOrg.Server.CreateHandler())
            .Route("vendor.test", vendor.Server.CreateHandler());
        using var downstream = new PaGettoApplication(
            _output,
            handler,
            upstreamSources: ["http://nuget.test/v3/index.json", "http://vendor.test/v3/index.json"]);
        using var client = downstream.CreateClient();

        Assert.Equal(@"{""versions"":[""1.2.3""]}", await client.GetStringAsync("v3/package/TestData/index.json"));
        Assert.Equal(@"{""versions"":[""1.0.0""]}", await client.GetStringAsync("v3/package/Vendor.Package/index.json"));

        using var nugetOrgPackage = await client.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg");
        using var vendorPackage = await client.GetAsync("v3/package/Vendor.Package/1.0.0/Vendor.Package.1.0.0.nupkg");

        Assert.Equal(HttpStatusCode.OK, nugetOrgPackage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, vendorPackage.StatusCode);
        Assert.Equal("http://nuget.test/v3/index.json", GetCachedFrom(downstream, "TestData"));
        Assert.Equal("http://vendor.test/v3/index.json", GetCachedFrom(downstream, "Vendor.Package"));
    }

    [Fact]
    public async Task SeveralUpstreamsMergeVersionsOfTheSamePackage()
    {
        using var first = new PaGettoApplication(_output);
        using var second = new PaGettoApplication(_output);
        await first.AddPackageAsync(BuildPackage("Shared.Package", "1.0.0"));
        await second.AddPackageAsync(BuildPackage("Shared.Package", "1.0.0"));
        await second.AddPackageAsync(BuildPackage("Shared.Package", "2.0.0"));

        var handler = new HostRoutingHandler()
            .Route("first.test", first.Server.CreateHandler())
            .Route("second.test", second.Server.CreateHandler());
        using var downstream = new PaGettoApplication(
            _output,
            handler,
            upstreamSources: ["http://first.test/v3/index.json", "http://second.test/v3/index.json"]);
        using var client = downstream.CreateClient();

        var content = await client.GetStringAsync("v3/package/Shared.Package/index.json");

        Assert.Equal(@"{""versions"":[""1.0.0"",""2.0.0""]}", content);
    }

    [Fact]
    public async Task UnreachableUpstreamIsSkipped()
    {
        using var vendor = new PaGettoApplication(_output);
        await vendor.AddPackageAsync(BuildPackage("Vendor.Package", "1.0.0"));

        // "down.test" is not routed, so every request to it fails with 503.
        var handler = new HostRoutingHandler().Route("vendor.test", vendor.Server.CreateHandler());
        using var downstream = new PaGettoApplication(
            _output,
            handler,
            upstreamSources: ["http://down.test/v3/index.json", "http://vendor.test/v3/index.json"]);
        using var client = downstream.CreateClient();

        using var response = await client.GetAsync("v3/package/Vendor.Package/1.0.0/Vendor.Package.1.0.0.nupkg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static MemoryStream BuildPackage(string id, string version)
    {
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = "Test description",
        };
        builder.Authors.Add("Test author");
        builder.DependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.Parse("net8.0"), []));
        builder.Files.Add(new PhysicalPackageFile(new MemoryStream()) { TargetPath = "lib/net8.0/_._" });

        var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static string GetCachedFrom(PaGettoApplication app, string packageId)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        return context.Packages.Single(p => p.Id == packageId).CachedFrom;
    }

    public void Dispose()
    {
        _upstream.Dispose();
        _downstream.Dispose();
    }
}
