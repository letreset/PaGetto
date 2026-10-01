using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Verifies Admin > Feeds > Settings: posted mirror rows are saved in list order,
/// missing rows are removed, blank secrets are kept, and invalid mirrors and numbers are rejected.
/// </summary>
public class FeedSettingsMirrorTests : IDisposable
{
    private const string AdminUsername = "mirroradmin";
    private const string AdminPassword = "MirrorAdminPass1!";
    private const string SettingsUrl = "/Admin/Feeds/default/Settings";

    private readonly PaGettoApplication _app;

    public FeedSettingsMirrorTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, null, dict => dict["Authentication:Mode"] = "Local");
    }

    [Fact]
    public async Task GetRendersStoredMirrorsInOrder()
    {
        await SeedMirrorsAsync();
        using var client = await SignInAsAdminAsync();

        var body = await client.GetStringAsync(SettingsUrl);

        var nugetOrg = body.IndexOf("https://api.nuget.org/v3/index.json", StringComparison.Ordinal);
        var vendor = body.IndexOf("https://vendor.test/v3/index.json", StringComparison.Ordinal);
        Assert.True(nugetOrg >= 0 && vendor > nugetOrg);
        Assert.Contains("A password is currently set.", body);
        Assert.DoesNotContain("vendor-secret", body);
    }

    [Fact]
    public async Task PostSavesReorderedListRemovesMissingAndKeepsBlankSecrets()
    {
        var (nugetOrgId, vendorId) = await SeedMirrorsAsync();
        using var client = await SignInAsAdminAsync();

        // Vendor moves first (password left blank), nuget.org is removed, a new mirror is added.
        var form = await BaseFormAsync(client);
        form.Add(new("Mirrors[0].Id", vendorId.ToString()));
        form.Add(new("Mirrors[0].Enabled", "true"));
        form.Add(new("Mirrors[0].PackageSource", "https://vendor.test/v3/index.json"));
        form.Add(new("Mirrors[0].AuthType", nameof(MirrorAuthenticationType.Basic)));
        form.Add(new("Mirrors[0].AuthUsername", "vendor-user"));
        form.Add(new("Mirrors[0].AuthPasswordNew", ""));
        form.Add(new("Mirrors[1].Id", ""));
        form.Add(new("Mirrors[1].PackageSource", "https://other.test/v3/index.json"));
        form.Add(new("Mirrors[1].AuthType", nameof(MirrorAuthenticationType.Bearer)));
        form.Add(new("Mirrors[1].AuthTokenNew", "other-token"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Settings saved.", body);

        var mirrors = await GetMirrorsAsync();
        Assert.Equal(2, mirrors.Count);
        Assert.DoesNotContain(mirrors, m => m.Id == nugetOrgId);

        Assert.Equal(vendorId, mirrors[0].Id);
        Assert.True(mirrors[0].Enabled);
        Assert.Equal("vendor-secret", mirrors[0].AuthPassword);

        Assert.Equal("https://other.test/v3/index.json", mirrors[1].PackageSource);
        Assert.False(mirrors[1].Enabled);
        Assert.Equal("other-token", mirrors[1].AuthToken);
    }

    [Fact]
    public async Task PostWithInvalidSourceIsRejected()
    {
        await SeedMirrorsAsync();
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.Add(new("Mirrors[0].Enabled", "true"));
        form.Add(new("Mirrors[0].PackageSource", "not a url"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Mirror 1: the package source must be an absolute http(s) URL.", body);
        Assert.Equal(2, (await GetMirrorsAsync()).Count);
    }

    [Fact]
    public async Task PostWithBlockedCustomHeaderIsRejected()
    {
        await SeedMirrorsAsync();
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.Add(new("Mirrors[0].PackageSource", "https://vendor.test/v3/index.json"));
        form.Add(new("Mirrors[1].PackageSource", "https://other.test/v3/index.json"));
        form.Add(new("Mirrors[1].AuthType", nameof(MirrorAuthenticationType.Custom)));
        form.Add(new("Mirrors[1].AuthCustomHeaders", "{\"Authorization\":\"Basic abc\"}"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Mirror 2: Custom headers may not include the security-sensitive header", body);
        Assert.Equal("https://api.nuget.org/v3/index.json", (await GetMirrorsAsync())[0].PackageSource);
    }

    [Fact]
    public async Task PostSavesUpstreamListingCacheOverride()
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key == "UseGlobalListingCache");
        form.Add(new("UpstreamListingCacheSeconds", "60"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Settings saved.", body);
        Assert.Equal(60, (await GetDefaultFeedAsync()).UpstreamListingCacheSeconds);
    }

    [Fact]
    public async Task PostWithGlobalUpstreamListingCacheClearsOverride()
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.Add(new("UpstreamListingCacheSeconds", "60"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await GetDefaultFeedAsync()).UpstreamListingCacheSeconds);
    }

    [Fact]
    public async Task PostWithNegativeUpstreamListingCacheIsRejected()
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key == "UseGlobalListingCache");
        form.Add(new("UpstreamListingCacheSeconds", "-1"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("The upstream listing cache duration must be 0 or more seconds.", body);
        Assert.Null((await GetDefaultFeedAsync()).UpstreamListingCacheSeconds);
    }

    [Theory]
    [InlineData("UseGlobalMaxSize", "MaxPackageSizeMiB", "-1", "The max package size must be at least 1 MiB.")]
    [InlineData("UseGlobalMaxSize", "MaxPackageSizeMiB", "0", "The max package size must be at least 1 MiB.")]
    [InlineData("UseGlobalMaxSize", "MaxPackageSizeMiB", "abc", "The value &#x27;abc&#x27; is not valid")]
    [InlineData("UseGlobalRetentionMajor", "RetentionMaxMajorVersions", "-3", "The number of major versions to keep must be 0 or more.")]
    [InlineData("UseGlobalRetentionPrerelease", "RetentionMaxPrereleaseVersions", "x", "The value &#x27;x&#x27; is not valid")]
    public async Task PostWithInvalidNumberIsRejectedAndNothingIsSaved(
        string useGlobalKey, string key, string value, string message)
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key == useGlobalKey);
        form.Add(new(key, value));
        form.RemoveAll(f => f.Key == "Name");
        form.Add(new("Name", "Renamed"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(message, body);
        Assert.DoesNotContain("Settings saved.", body);
        var feed = await GetDefaultFeedAsync();
        Assert.NotEqual("Renamed", feed.Name);
        Assert.Null(feed.MaxPackageSizeMiB);
        Assert.Null(feed.RetentionMaxMajorVersions);
    }

    [Fact]
    public async Task PostWithNegativeMirrorTimeoutIsRejected()
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.Add(new("Mirrors[0].Id", ""));
        form.Add(new("Mirrors[0].PackageSource", "https://other.test/v3/index.json"));
        form.Add(new("Mirrors[0].DownloadTimeoutSeconds", "-5"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Mirror 1: the download timeout must be at least 1 second", body);
        Assert.Empty(await GetMirrorsAsync());
    }

    [Fact]
    public async Task PostWithValidNumbersSavesThem()
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key is "UseGlobalMaxSize" or "UseGlobalRetentionMajor");
        form.Add(new("MaxPackageSizeMiB", "2"));
        form.Add(new("RetentionMaxMajorVersions", "3"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Contains("Settings saved.", await response.Content.ReadAsStringAsync());
        var feed = await GetDefaultFeedAsync();
        Assert.Equal(2u, feed.MaxPackageSizeMiB);
        Assert.Equal(3, feed.RetentionMaxMajorVersions);
    }

    [Fact]
    public async Task GetRendersReadOnlyModeAsSwitch()
    {
        using var client = await SignInAsAdminAsync();

        var body = await client.GetStringAsync(SettingsUrl);

        Assert.Matches("<input type=\"checkbox\"[^>]*id=\"IsReadOnlyMode\" name=\"IsReadOnlyMode\" value=\"true\"", body);
        Assert.Contains("<input type=\"hidden\" name=\"IsReadOnlyMode\" value=\"false\" />", body);
    }

    [Theory]
    [InlineData(new[] { "true", "false" }, true)]
    [InlineData(new[] { "false" }, false)]
    public async Task PostSavesReadOnlyOverride(string[] postedValues, bool expected)
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key == "UseGlobalReadOnly");
        form.Add(new("UseGlobalReadOnly", "false"));
        form.AddRange(postedValues.Select(v => new KeyValuePair<string, string>("IsReadOnlyMode", v)));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Contains("Settings saved.", await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, (await GetDefaultFeedAsync()).IsReadOnlyMode);
        Assert.Contains(
            expected ? "value=\"true\" checked=\"checked\"" : "name=\"IsReadOnlyMode\" value=\"true\" data-global-value",
            await client.GetStringAsync(SettingsUrl));
    }

    [Fact]
    public async Task PostWithGlobalReadOnlyClearsOverride()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
            var feed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
            feed.IsReadOnlyMode = true;
            await feedService.UpdateFeedAsync(feed, CancellationToken.None);
        }
        using var client = await SignInAsAdminAsync();

        // A disabled switch isn't posted, so only the hidden "false" arrives.
        var form = await BaseFormAsync(client);
        form.Add(new("IsReadOnlyMode", "false"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Contains("Settings saved.", await response.Content.ReadAsStringAsync());
        Assert.Null((await GetDefaultFeedAsync()).IsReadOnlyMode);
    }

    [Theory]
    [InlineData(new[] { "true", "false" }, true)]
    [InlineData(new[] { "false" }, false)]
    public async Task PostSavesDeletePrereleasesOfOlderMajorsOverride(string[] postedValues, bool expected)
    {
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.RemoveAll(f => f.Key == "UseGlobalRetentionOlderMajorPrereleases");
        form.Add(new("UseGlobalRetentionOlderMajorPrereleases", "false"));
        form.AddRange(postedValues.Select(v => new KeyValuePair<string, string>("RetentionDeletePrereleasesOfOlderMajors", v)));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Contains("Settings saved.", await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, (await GetDefaultFeedAsync()).RetentionDeletePrereleasesOfOlderMajors);
        Assert.Matches(
            expected
                ? "id=\"RetentionDeletePrereleasesOfOlderMajors\" name=\"RetentionDeletePrereleasesOfOlderMajors\" value=\"true\" checked=\"checked\""
                : "id=\"RetentionDeletePrereleasesOfOlderMajors\" name=\"RetentionDeletePrereleasesOfOlderMajors\" value=\"true\" data-global-value",
            await client.GetStringAsync(SettingsUrl));
    }

    [Fact]
    public async Task PostWithGlobalDeletePrereleasesOfOlderMajorsClearsOverride()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
            var feed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
            feed.RetentionDeletePrereleasesOfOlderMajors = true;
            await feedService.UpdateFeedAsync(feed, CancellationToken.None);
        }
        using var client = await SignInAsAdminAsync();

        var form = await BaseFormAsync(client);
        form.Add(new("RetentionDeletePrereleasesOfOlderMajors", "false"));

        using var response = await client.PostAsync(SettingsUrl, new FormUrlEncodedContent(form));

        Assert.Contains("Settings saved.", await response.Content.ReadAsStringAsync());
        Assert.Null((await GetDefaultFeedAsync()).RetentionDeletePrereleasesOfOlderMajors);
    }

    private async Task<Feed> GetDefaultFeedAsync()
    {
        using var scope = _app.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        return await feedService.GetDefaultFeedAsync(CancellationToken.None);
    }

    private async Task<(int nugetOrgId, int vendorId)> SeedMirrorsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feedService.GetDefaultFeedAsync(CancellationToken.None);

        var nugetOrg = new FeedMirror { SortOrder = 0, Enabled = true, PackageSource = "https://api.nuget.org/v3/index.json" };
        var vendor = new FeedMirror
        {
            SortOrder = 1,
            Enabled = true,
            PackageSource = "https://vendor.test/v3/index.json",
            AuthType = MirrorAuthenticationType.Basic,
            AuthUsername = "vendor-user",
            AuthPassword = "vendor-secret",
        };
        feed.Mirrors.Add(nugetOrg);
        feed.Mirrors.Add(vendor);
        await feedService.UpdateFeedAsync(feed, CancellationToken.None);

        return (nugetOrg.Id, vendor.Id);
    }

    private async Task<List<FeedMirror>> GetMirrorsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
        var feed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
        return feed.Mirrors.OrderBy(m => m.SortOrder).ToList();
    }

    private async Task<HttpClient> SignInAsAdminAsync()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
            var admin = await userService.CreateLocalUserAsync(
                AdminUsername, "Admin", null, AdminPassword, canLoginToUI: true,
                createdByUserId: null, CancellationToken.None);
            await userService.SetAdminAsync(admin.Id, true, CancellationToken.None);
        }

        var client = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var login = new List<KeyValuePair<string, string>>
        {
            new("Username", AdminUsername),
            new("Password", AdminPassword),
            new("__RequestVerificationToken", ExtractAntiforgeryToken(await client.GetStringAsync("/Login"))),
        };
        using var response = await client.PostAsync("/Login", new FormUrlEncodedContent(login));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    /// <summary>The non-mirror fields of the settings form, all left on their global defaults.</summary>
    private static async Task<List<KeyValuePair<string, string>>> BaseFormAsync(HttpClient client)
    {
        return
        [
            new("__RequestVerificationToken", ExtractAntiforgeryToken(await client.GetStringAsync(SettingsUrl))),
            new("Name", "Default"),
            new("UseGlobalReadOnly", "true"),
            new("UseGlobalOverwrite", "true"),
            new("UseGlobalDeletion", "true"),
            new("UseGlobalMaxSize", "true"),
            new("UseGlobalRetentionMajor", "true"),
            new("UseGlobalRetentionMinor", "true"),
            new("UseGlobalRetentionPatch", "true"),
            new("UseGlobalRetentionPrerelease", "true"),
            new("UseGlobalRetentionOlderMajorPrereleases", "true"),
            new("UseGlobalListingCache", "true"),
        ];
    }

    private static string ExtractAntiforgeryToken(string body)
    {
        var idx = body.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        var valueIdx = body.IndexOf("value=\"", idx, StringComparison.Ordinal) + "value=\"".Length;
        return body[valueIdx..body.IndexOf('"', valueIdx)];
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
