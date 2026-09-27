using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The Manage section of the package page, through the real routing.
/// </summary>
public class WebUiPackageManageTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiPackageManageTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Fact]
    public async Task DeleteRedirectsToThePackageWithoutTheDeletedVersion()
    {
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync("/packages/TestData/1.2.3", "Delete", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/packages/TestData", response.Headers.Location?.OriginalString, ignoreCase: true);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
