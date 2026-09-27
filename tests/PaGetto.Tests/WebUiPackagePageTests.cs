using System;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The rendered package page, through the real routing.
/// </summary>
public class WebUiPackagePageTests : IDisposable
{
    private const string XssPayload = "\"><script>alert(1)</script>";

    private readonly PaGettoApplication _app;

    public WebUiPackagePageTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
    }

    [Fact]
    public async Task RendersTheInstallSnippets()
    {
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync("/packages/TestData/1.2.3");

        Assert.Contains("\"dotnet add package TestData --version 1.2.3\"", html);
        Assert.Contains("\"NuGet\\\\Install-Package TestData -Version 1.2.3\"", html);
        Assert.Contains("\"<PackageVersion Include=\\\"TestData\\\" Version=\\\"1.2.3\\\" />\"", html);
        Assert.Contains("\"<PackageReference Include=\\\"TestData\\\" />\"", html);
        Assert.Contains("\"#r \\\"nuget: TestData, 1.2.3\\\"\"", html);
        Assert.Contains("\"#:package TestData@1.2.3\"", html);
        Assert.Contains("\"#addin nuget:?package=TestData&version=1.2.3\"", html);
        Assert.Contains("\"#tool nuget:?package=TestData&version=1.2.3\"", html);
    }

    [Fact]
    public async Task ShowsNoPrereleaseNoteForAStableVersion()
    {
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync("/packages/TestData/1.2.3");

        Assert.Contains("Per day", html);
        Assert.DoesNotContain("This is a prerelease version", html);
        Assert.DoesNotContain("Include prerelease", html);
    }

    [Fact]
    public async Task EncodesAnUnknownPackageIdFromTheUrl()
    {
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync("/packages/" + Uri.EscapeDataString(XssPayload));

        Assert.Contains("Oops, package not found...", html);
        Assert.DoesNotContain(XssPayload, html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task EncodesAMissingVersionFromTheUrl()
    {
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        using var client = _app.CreateClient();

        var html = await client.GetStringAsync("/packages/TestData/" + Uri.EscapeDataString(XssPayload));

        Assert.Contains("Version not found", html);
        Assert.DoesNotContain(XssPayload, html);
        Assert.Contains("&lt;script&gt;", html);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
