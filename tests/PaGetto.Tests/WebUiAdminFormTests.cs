using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Admin pages bind their create forms for every POST; other actions must not report those
/// forms' required fields as missing.
/// </summary>
public class WebUiAdminFormTests : IDisposable
{
    private const string Password = "LocalPassword123!";

    private readonly PaGettoApplication _app;

    public WebUiAdminFormTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
        });
    }

    [Theory]
    [InlineData("/Admin/Feeds", "Delete", "feedId", "Slug is required.")]
    [InlineData("/Admin/Groups", "DeleteGroup", "groupId", "Group name is required.")]
    [InlineData("/Admin/Accounts", "Delete", "userId", "Username is required.")]
    public async Task OtherActionsDontValidateTheCreateForm(string page, string handler, string idField, string message)
    {
        await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
        using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);

        using var response = await admin.PostFormAsync(page, handler, new Dictionary<string, string>
        {
            { idField, Guid.NewGuid().ToString() },
        });

        // The message also sits in the data-val-required attribute for client-side validation;
        // only a rendered validation message (element text) means the server reported it.
        Assert.DoesNotContain(">" + message + "<", await response.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
