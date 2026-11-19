using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The admin page that lists the known vulnerabilities across feeds.
/// </summary>
public class WebUiVulnerabilitiesTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiVulnerabilitiesTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict => dict["Authentication:Mode"] = "Local");
    }

    [Fact]
    public async Task ListsFindingsFilteredBySeverity()
    {
        await SeedFindingsAsync();
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        var all = await admin.GetStringAsync("/Admin/Vulnerabilities");
        Assert.Contains("GHSA-crit", all, StringComparison.Ordinal);
        Assert.Contains("GHSA-low", all, StringComparison.Ordinal);

        var high = await admin.GetStringAsync("/Admin/Vulnerabilities?severity=High");
        Assert.Contains("GHSA-crit", high, StringComparison.Ordinal);
        Assert.DoesNotContain("GHSA-low", high, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportsCsvAndJson()
    {
        await SeedFindingsAsync();
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        var csv = await admin.GetStringAsync("/Admin/Vulnerabilities?handler=Export&format=csv&severity=Critical");
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("feed,package,version,advisory,", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("default,Contoso.Lib,1.0.0,GHSA-crit,,Critical,9.8,\"Bad, very bad\",<1.0.1,1.0.1,", lines[1], StringComparison.Ordinal);

        using var json = JsonDocument.Parse(await admin.GetStringAsync("/Admin/Vulnerabilities?handler=Export&format=json"));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("GHSA-crit", json.RootElement[0].GetProperty("advisory").GetString());
        Assert.Equal("2020-01-01T00:00:00Z", json.RootElement[0].GetProperty("foundAtUtc").GetString());
    }

    [Fact]
    public async Task OnlyAdminsSeeThePage()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "user", Password);
        using var user = await WebUiSession.SignInAsync(_app, "user", Password);

        using var response = await user.Client.GetAsync("/Admin/Vulnerabilities?handler=Export&format=json");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose()
    {
        _app.Dispose();
    }

    private async Task SeedFindingsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var feed = await scope.ServiceProvider.GetRequiredService<IFeedService>().GetDefaultFeedAsync(CancellationToken.None);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        context.PackageVulnerabilities.Add(new PackageVulnerability
        {
            FeedId = feed.Id,
            PackageName = "Contoso.Lib",
            PackageVersion = "1.0.0",
            AdvisoryId = "GHSA-crit",
            Summary = "Bad, very bad",
            Severity = VulnerabilitySeverity.Critical,
            CvssScore = 9.8,
            AffectedVersions = "<1.0.1",
            FixedVersion = "1.0.1",
            FoundAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        context.PackageVulnerabilities.Add(new PackageVulnerability
        {
            FeedId = feed.Id,
            PackageName = "Contoso.Other",
            PackageVersion = "2.0.0",
            AdvisoryId = "GHSA-low",
            Severity = VulnerabilitySeverity.Low,
            FoundAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        await context.SaveChangesAsync(CancellationToken.None);
    }
}
