using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
/// The admin page of the malicious packages: the lookup and the allowances.
/// </summary>
public class WebUiMaliciousPackagesTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiMaliciousPackagesTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict => dict["Authentication:Mode"] = "Local");
    }

    [Fact]
    public async Task LooksUpAListedPackage()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            context.MaliciousPackages.Add(new MaliciousPackage
            {
                Ecosystem = "PyPI",
                Name = "evil-pkg",
                AdvisoryId = "MAL-2099-0007",
                Summary = "Malicious code in evil-pkg (PyPI)",
                ModifiedUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        // PyPI names are matched normalized.
        var page = await admin.GetStringAsync("/Admin/MaliciousPackages?ecosystem=PyPI&name=Evil_Pkg");

        Assert.Contains("MAL-2099-0007", page, StringComparison.Ordinal);
        Assert.Contains("every version", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsAndBlocksAgainWithAuditEvents()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using (var allow = await admin.PostFormAsync("/Admin/MaliciousPackages", "Allow", new Dictionary<string, string>
        {
            ["AllowEcosystem"] = "npm",
            ["AllowName"] = "Evil-Pkg",
            ["AllowVersion"] = "1.0.0",
            ["AllowReason"] = "False positive",
        }))
        {
            Assert.Equal(HttpStatusCode.OK, allow.StatusCode);
            Assert.Contains("npm:evil-pkg@1.0.0 is allowed.", await allow.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        int key;
        using (var scope = _app.Services.CreateScope())
        {
            var allowance = await scope.ServiceProvider.GetRequiredService<IContext>().MaliciousPackageAllowances.SingleAsync();
            Assert.Equal("admin", allowance.CreatedBy);
            key = allowance.Key;
        }

        using (var remove = await admin.PostFormAsync("/Admin/MaliciousPackages", "Remove", new Dictionary<string, string> { ["key"] = key.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
        {
            Assert.Contains("is no longer allowed.", await remove.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            Assert.Empty(await context.MaliciousPackageAllowances.ToListAsync());

            var events = await context.AuditEvents.Where(e => e.Event.StartsWith("malicious_package_")).OrderBy(e => e.Event).ToListAsync();
            Assert.Equal(["malicious_package_allowance_removed", "malicious_package_allowed"], events.Select(e => e.Event));
            Assert.All(events, e => Assert.Equal("npm:evil-pkg@1.0.0", e.Target));
            Assert.Equal("reason=False positive", events[1].Detail);
        }
    }

    [Fact]
    public async Task RequiresAReason()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var allow = await admin.PostFormAsync("/Admin/MaliciousPackages", "Allow", new Dictionary<string, string>
        {
            ["AllowEcosystem"] = "npm",
            ["AllowName"] = "evil-pkg",
        });

        Assert.Contains("Say why the package is allowed.", await allow.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAdminsSeeThePage()
    {
        await WebUiSession.SeedLocalUserAsync(_app, "user", Password);
        using var user = await WebUiSession.SignInAsync(_app, "user", Password);

        using var response = await user.Client.GetAsync("/Admin/MaliciousPackages");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
