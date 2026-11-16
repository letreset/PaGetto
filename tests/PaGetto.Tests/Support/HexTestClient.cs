using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Hex;
using PaGetto.Core.Tests.Support;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of Hex feeds share: creating a feed, and the requests Mix and Rebar3 send.
/// </summary>
internal static class HexTestClient
{
    public const string ErlangContentType = "application/vnd.hex+erlang";

    /// <summary>
    /// Creates a Hex feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateHexFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.Hex };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    public static string RepositoryUrl(string feedSlug)
    {
        return $"feeds/{feedSlug}/hex";
    }

    public static string TarballUrl(string feedSlug, string name, string version)
    {
        return $"{RepositoryUrl(feedSlug)}/tarballs/{name}-{version}.tar";
    }

    /// <summary>
    /// Publishes a tarball the way <c>mix hex.publish</c> does.
    /// </summary>
    public static async Task<HttpResponseMessage> PublishHexAsync(this HttpClient client, string feedSlug, string name, byte[] tarball, bool replace = false)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{RepositoryUrl(feedSlug)}/api/packages/{name}/releases?replace={(replace ? "true" : "false")}");
        request.Content = new ByteArrayContent(tarball);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Accept.ParseAdd(ErlangContentType);

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PublishHexAsync(this HttpClient client, string feedSlug, string name, string version)
    {
        return client.PublishHexAsync(feedSlug, name, HexTarballBuilder.Create(name, version));
    }

    /// <summary>
    /// Retires a version the way <c>mix hex.retire</c> does: the reason and message as a map in
    /// the external term format.
    /// </summary>
    public static async Task<HttpResponseMessage> RetireHexAsync(this HttpClient client, string feedSlug, string name, string version, string reason, string message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{RepositoryUrl(feedSlug)}/api/packages/{name}/releases/{version}/retire");
        request.Content = new ByteArrayContent(ErlangExternalTerm.Encode(new Dictionary<string, object> { ["reason"] = reason, ["message"] = message }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ErlangContentType);

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> UnretireHexAsync(this HttpClient client, string feedSlug, string name, string version)
    {
        return client.DeleteAsync($"{RepositoryUrl(feedSlug)}/api/packages/{name}/releases/{version}/retire");
    }

    /// <summary>
    /// The feed's public key, which the registry is checked with.
    /// </summary>
    public static async Task<RSA> GetPublicKeyAsync(this HttpClient client, string feedSlug)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(await client.GetStringAsync($"{RepositoryUrl(feedSlug)}/public_key"));
        return rsa;
    }

    /// <summary>
    /// A registry resource, with its signature checked.
    /// </summary>
    public static async Task<byte[]> GetRegistryPayloadAsync(this HttpClient client, string feedSlug, string path)
    {
        using var key = await client.GetPublicKeyAsync(feedSlug);
        return HexRegistry.ReadPayload(await client.GetByteArrayAsync($"{RepositoryUrl(feedSlug)}/{path}"), key);
    }

    /// <summary>
    /// The versions of a package's registry resource, with its signature checked.
    /// </summary>
    public static async Task<IReadOnlyList<HexRelease>> GetReleasesAsync(this HttpClient client, string feedSlug, string name)
    {
        return HexRegistry.ReadPackage(await client.GetRegistryPayloadAsync(feedSlug, $"packages/{name}"), name);
    }

    /// <summary>
    /// The body of an API response, in the external term format.
    /// </summary>
    public static async Task<Dictionary<string, object>> ReadHexTermAsync(this HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != ErlangContentType)
        {
            throw new InvalidOperationException($"The response is {response.Content.Headers.ContentType}, not {ErlangContentType}.");
        }

        return (Dictionary<string, object>)ErlangExternalTerm.Decode(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// The message Hex shows for an error response.
    /// </summary>
    public static async Task<string> ReadHexErrorAsync(this HttpResponseMessage response)
    {
        return (string)(await response.ReadHexTermAsync())["message"];
    }

    /// <summary>
    /// Sends an API key from now on the way Hex does: as the whole <c>Authorization</c> header.
    /// </summary>
    public static void UseHexKey(this HttpClient client, string key)
    {
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", key);
    }
}
