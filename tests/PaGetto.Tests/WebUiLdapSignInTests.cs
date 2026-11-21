using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PaGetto.Core.Authentication;
using PaGetto.Core.Authentication.Ldap;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Sign-in with the username and password of an LDAP directory (<c>Authentication:Ldap</c>), in the
/// web UI and from client tools. The directory is replaced by a fake that knows the user
/// <c>fry</c> with the password <c>fry</c>, in the directory group <c>ship_crew</c>.
/// </summary>
public class WebUiLdapSignInTests : IDisposable
{
    private const string Password = "fry";

    private readonly WebApplicationFactory<Startup> _app;
    private readonly Mock<ILdapDirectory> _directory = new();

    public WebUiLdapSignInTests(ITestOutputHelper output)
    {
        _directory
            .Setup(d => d.AuthenticateAsync(It.Is<string>(u => u.Equals("fry", StringComparison.OrdinalIgnoreCase)), Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LdapUser(
                "uuid-fry",
                "cn=Philip J. Fry,ou=people,dc=planetexpress,dc=com",
                "fry",
                "Philip J. Fry",
                "fry@planetexpress.com",
                ["ship_crew", "cn=ship_crew,ou=people,dc=planetexpress,dc=com"]));

        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
            dict["Authentication:Ldap:Server"] = "ldap.example.test";
            dict["Authentication:Ldap:SearchBase"] = "ou=people,dc=planetexpress,dc=com";
            dict["Authentication:Ldap:DisplayName"] = "Planet Express";
        }).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(_directory.Object)));
    }

    [Fact]
    public async Task LoginPageNamesTheDirectory()
    {
        using var client = CreateClient();

        var body = await client.GetStringAsync("/Login");

        Assert.Contains("Use your Planet Express or local account to continue.", body);
        Assert.Contains("type=\"password\"", body);
    }

    [Fact]
    public async Task DirectoryUserSignsIn()
    {
        using var client = CreateClient();

        using var response = await SignInAsync(client, "Fry", Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthenticationConstants.CookieName + "=", StringComparison.Ordinal));

        var user = await FindUserAsync("fry");
        Assert.Equal(AuthProvider.Ldap, user.AuthProvider);
        Assert.Equal("Philip J. Fry", user.DisplayName);

        using var tokens = await client.GetAsync("/Account/Tokens");
        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode);
    }

    [Fact]
    public async Task WrongDirectoryPasswordIsRefused()
    {
        using var client = CreateClient();

        using var response = await SignInAsync(client, "fry", "bender");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DirectoryGroupsGrantFeedPermissionsToClientTools()
    {
        await GrantPullToGroupAsync("ship_crew");
        using var client = CreateClient();

        client.UseBasicCredentials("fry", Password);
        using var allowed = await client.GetAsync("/v3/search?q=a");
        client.UseBasicCredentials("fry", "bender");
        using var refused = await client.GetAsync("/v3/search?q=a");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private HttpClient CreateClient()
    {
        return _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string username, string password)
    {
        var page = await client.GetStringAsync("/Login");
        const string field = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = page.IndexOf(field, StringComparison.Ordinal) + field.Length;
        var token = page[start..page.IndexOf('"', start)];

        return await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private async Task<User> FindUserAsync(string username)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        return await users.FindByUsernameAsync(username, CancellationToken.None);
    }

    private async Task GrantPullToGroupAsync(string roleValue)
    {
        using var scope = _app.Services.CreateScope();
        var groups = scope.ServiceProvider.GetRequiredService<IGroupService>();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var feeds = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var group = await groups.CreateGroupAsync("Crew", roleValue, null, CancellationToken.None);
        var feed = await feeds.GetDefaultFeedAsync(CancellationToken.None);
        await permissions.GrantPermissionAsync(group.Id, PrincipalType.Group, feed.Id, canPush: false, canPull: true, CancellationToken.None);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
