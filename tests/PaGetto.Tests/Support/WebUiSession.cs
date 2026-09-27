using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Tests.Support;

/// <summary>
/// A cookie-tracking client for web UI tests: seeds local accounts, signs in through the
/// login page and posts Razor page forms with their antiforgery token.
/// </summary>
public sealed class WebUiSession : IDisposable
{
    private WebUiSession(HttpClient client)
    {
        Client = client;
    }

    public HttpClient Client { get; }

    /// <summary>
    /// Creates a local account that can sign in to the web UI, with pull access to the default feed.
    /// </summary>
    public static async Task<User> SeedLocalUserAsync(
        PaGettoApplication app,
        string username,
        string password,
        bool isAdmin = false,
        bool canLoginToUI = true,
        bool canPush = false)
    {
        using var scope = app.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var user = await userService.CreateLocalUserAsync(
            username, username, null, password, canLoginToUI, createdByUserId: null, CancellationToken.None);

        if (isAdmin)
        {
            await userService.SetAdminAsync(user.Id, true, CancellationToken.None);
        }

        var defaultFeed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
        await permissionService.GrantPermissionAsync(
            user.Id, PrincipalType.User, defaultFeed.Id, canPush: canPush, canPull: true, CancellationToken.None);

        return user;
    }

    /// <summary>
    /// Signs in through the login page. The returned session doesn't follow redirects.
    /// </summary>
    public static async Task<WebUiSession> SignInAsync(PaGettoApplication app, string username, string password)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var session = new WebUiSession(client);

        using var response = await session.PostFormAsync("/Login", null, new Dictionary<string, string>
        {
            { "Username", username },
            { "Password", password },
        });

        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            session.Dispose();
            throw new InvalidOperationException($"Signing in as '{username}' failed with {response.StatusCode}");
        }

        return session;
    }

    public async Task<string> GetStringAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Loads <paramref name="pagePath"/> for its antiforgery token, then posts the form to
    /// the given handler of the same page.
    /// </summary>
    public async Task<HttpResponseMessage> PostFormAsync(
        string pagePath, string handler, IDictionary<string, string> values)
    {
        var body = await GetStringAsync(pagePath);
        var form = new Dictionary<string, string>(values);
        var token = ExtractAntiforgeryToken(body);
        if (token != null)
        {
            form["__RequestVerificationToken"] = token;
        }

        var separator = pagePath.Contains('?') ? "&" : "?";
        var url = handler == null ? pagePath : $"{pagePath}{separator}handler={handler}";
        return await Client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// Loads <paramref name="pagePath"/> for its antiforgery token, then posts <paramref name="file"/>
    /// as multipart form data to the given handler, with the token in the
    /// <c>RequestVerificationToken</c> header as the upload page's script sends it. Pass
    /// <paramref name="tokenPagePath"/> to take the token from another page.
    /// </summary>
    public async Task<HttpResponseMessage> PostFileAsync(
        string pagePath, string handler, Stream file, string fileName, bool includeToken = true, string tokenPagePath = null)
    {
        var body = await GetStringAsync(tokenPagePath ?? pagePath);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{pagePath}?handler={handler}");
        if (includeToken)
        {
            request.Headers.Add("RequestVerificationToken", ExtractAntiforgeryToken(body));
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(file), "package", fileName);
        request.Content = content;
        return await Client.SendAsync(request);
    }

    private static string ExtractAntiforgeryToken(string body)
    {
        const string tokenFieldName = "__RequestVerificationToken";
        var idx = body.IndexOf(tokenFieldName, StringComparison.Ordinal);
        if (idx < 0) return null;

        var valueIdx = body.IndexOf("value=\"", idx, StringComparison.Ordinal);
        if (valueIdx < 0) return null;

        valueIdx += "value=\"".Length;
        var endIdx = body.IndexOf('"', valueIdx);
        return endIdx < 0 ? null : body[valueIdx..endIdx];
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
