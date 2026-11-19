using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.Tests.Support;
using PaGetto.Core.VsCode;
using Xunit;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of VS Code feeds share: creating a feed, the queries of VS Code, and uploads.
/// </summary>
internal static class VsCodeTestClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a VS Code feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreateVsCodeFeedAsync(this WebApplicationFactory<Startup> factory, string slug, string mirrorSource = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.VsCode };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    /// <summary>
    /// Posts a query as VS Code does: the criteria, the target and the flags VS Code sends.
    /// </summary>
    public static async Task<HttpResponseMessage> PostVsCodeQueryAsync(
        this HttpClient client,
        string galleryPath,
        IEnumerable<(int FilterType, string Value)> criteria,
        int flags = 0x1 | 0x2 | 0x10 | 0x80 | 0x100 | 0x20,
        int pageNumber = 1,
        int pageSize = 50,
        int sortBy = 0)
    {
        var query = new VsCodeGalleryQuery
        {
            Filters =
            [
                new VsCodeGalleryQueryFilter
                {
                    Criteria = criteria
                        .Select(c => new VsCodeGalleryCriterion { FilterType = c.FilterType, Value = c.Value })
                        .Append(new VsCodeGalleryCriterion { FilterType = VsCodeGalleryApi.TargetFilter, Value = VsCodeGalleryApi.InstallationTarget })
                        .ToList(),
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = sortBy,
                },
            ],
            Flags = flags,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{galleryPath}/extensionquery")
        {
            Content = JsonContent.Create(query, options: _jsonOptions),
        };
        request.Headers.TryAddWithoutValidation("Accept", VsCodeGalleryApi.QueryAcceptHeader);

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Queries a feed's gallery and reads the result.
    /// </summary>
    public static async Task<VsCodeGalleryResult> QueryVsCodeAsync(
        this HttpClient client,
        string feedSlug,
        IEnumerable<(int FilterType, string Value)> criteria,
        int flags = 0x1 | 0x2 | 0x10 | 0x80 | 0x100 | 0x20,
        int pageNumber = 1,
        int pageSize = 50,
        string token = null)
    {
        var gallery = token == null ? $"feeds/{feedSlug}/vscode/gallery" : $"feeds/{feedSlug}/vscode/auth/{token}/gallery";
        using var response = await client.PostVsCodeQueryAsync(gallery, criteria, flags, pageNumber, pageSize);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VsCodeGalleryQueryResult>(_jsonOptions);
        return Assert.Single(result!.Results);
    }

    /// <summary>
    /// Finds one extension by its identifier, as VS Code does before it installs it.
    /// </summary>
    public static async Task<VsCodeGalleryExtension> FindVsCodeExtensionAsync(this HttpClient client, string feedSlug, string id, string token = null)
    {
        var result = await client.QueryVsCodeAsync(feedSlug, [(VsCodeGalleryApi.ExtensionNameFilter, id)], token: token);
        return result.Extensions.SingleOrDefault();
    }

    /// <summary>
    /// Uploads a VSIX file as <c>curl -T</c> does.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadVsCodeAsync(this HttpClient client, string feedSlug, byte[] vsix)
    {
        var content = new ByteArrayContent(vsix);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.PutAsync($"feeds/{feedSlug}/vscode/upload", content);
    }

    /// <summary>
    /// The URL VS Code reads an asset of a gallery version from, with the platform whenever the version has one.
    /// </summary>
    public static string GetAssetUrl(this VsCodeGalleryVersion version, string assetType)
    {
        Assert.Contains(version.Files, f => f.AssetType == assetType);
        var url = $"{version.AssetUri}/{assetType}";
        return string.IsNullOrEmpty(version.TargetPlatform) ? url : $"{url}?targetPlatform={version.TargetPlatform}";
    }
}
