using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Npm;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.VsCode;

public partial class VsCodePackageService : IVsCodePackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly VsCodeUpstreamClient _upstream;
    private readonly PackageMirrorLock _lock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<VsCodePackageService> _logger;

    public VsCodePackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        VsCodeUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<VsCodePackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _lock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VsCodeGalleryQueryResult> QueryAsync(Feed feed, VsCodeGalleryQuery query, string galleryUrl, CancellationToken cancellationToken)
    {
        var filter = query.Filters?.FirstOrDefault() ?? new VsCodeGalleryQueryFilter();
        var criteria = filter.Criteria ?? [];

        var names = criteria
            .Where(c => c.FilterType == VsCodeGalleryApi.ExtensionNameFilter && !string.IsNullOrEmpty(c.Value))
            .Select(c => c.Value)
            .ToList();
        var uuids = criteria
            .Where(c => c.FilterType == VsCodeGalleryApi.ExtensionIdFilter)
            .Select(c => Guid.TryParse(c.Value, out var uuid) ? uuid : Guid.Empty)
            .Where(uuid => uuid != Guid.Empty)
            .ToHashSet();

        if (names.Count == 0 && uuids.Count == 0)
        {
            return await SearchAsync(feed, query, filter, galleryUrl, cancellationToken);
        }

        // Identifiers are the UUIDs this server gives the feed's own extensions, or the UUIDs the
        // mirror gives its extensions. VS Code asks by name for those it doesn't get back.
        if (uuids.Count > 0)
        {
            var stored = (await _artifacts.FindNamesAsync(feed.Id, cancellationToken))
                .Where(name => uuids.Remove(VsCodeExtensionId.GetUuid("extension", name)))
                .ToList();
            names.AddRange(stored);

            foreach (var uuid in uuids)
            {
                if (await _upstream.FindAsync(feed, uuid, cancellationToken) is { } upstream)
                {
                    names.Add($"{upstream.Extension.Publisher.PublisherName}.{upstream.Extension.ExtensionName}");
                }
            }
        }

        var extensions = new List<VsCodeGalleryExtension>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extension = await GetExtensionOrNullAsync(feed, name, galleryUrl, cancellationToken);
            if (extension != null)
            {
                extensions.Add(FilterVersions(extension, query.Flags));
            }
        }

        return VsCodeGalleryQueryResult.Create(extensions, extensions.Count);
    }

    public async Task<VsCodeGalleryExtension> GetLatestOrNullAsync(Feed feed, string id, string galleryUrl, CancellationToken cancellationToken)
    {
        var extension = await GetExtensionOrNullAsync(feed, id, galleryUrl, cancellationToken);

        return extension == null
            ? null
            : FilterVersions(extension, VsCodeGalleryApi.IncludeLatestPrereleaseAndStableVersionOnlyFlag);
    }

    public async Task<VsCodeAsset> GetAssetOrNullAsync(Feed feed, string id, string version, string targetPlatform, string assetType, CancellationToken cancellationToken)
    {
        if (!VsCodeExtensionId.TryParse(id, out _, out _)
            || !NpmVersion.TryParse(version, out _)
            || !VsCodeTargetPlatform.TryParse(targetPlatform, out var platform)
            || string.IsNullOrEmpty(assetType))
        {
            return null;
        }

        var key = VsCodeExtensionId.Normalize(id);
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, key, version, cancellationToken);
        var manifest = stored == null ? null : VsCodeVersionManifest.Parse(stored.MetadataJson).Find(platform);

        if (manifest == null)
        {
            if (!string.Equals(assetType, VsCodeGalleryApi.VsixPackageAsset, StringComparison.OrdinalIgnoreCase))
            {
                return await GetUpstreamAssetOrNullAsync(feed, id, version, platform, assetType, cancellationToken);
            }

            (stored, manifest) = await MirrorAsync(feed, id, version, platform, cancellationToken);
            if (manifest == null)
            {
                return null;
            }
        }

        if (string.Equals(assetType, VsCodeGalleryApi.VsixPackageAsset, StringComparison.OrdinalIgnoreCase))
        {
            var file = await _storage.GetFileStreamAsync(feed.Slug, key, stored.Version, manifest.FileName, cancellationToken);
            if (file == null)
            {
                return null;
            }

            await _artifacts.AddDownloadAsync(feed.Id, key, stored.Version, cancellationToken);

            return new VsCodeAsset { Content = file, ContentType = "application/octet-stream", FileName = manifest.FileName };
        }

        var path = string.Equals(assetType, VsCodeGalleryApi.VsixManifestAsset, StringComparison.OrdinalIgnoreCase)
            ? VsCodeArchiveReader.ManifestFileName
            : manifest.Assets.GetValueOrDefault(assetType);
        if (path == null)
        {
            // A file stored from a mirror has the mirror's signature, which isn't part of the file.
            return stored.CachedFrom == null
                ? null
                : await GetUpstreamAssetOrNullAsync(feed, id, stored.Version, platform, assetType, cancellationToken);
        }

        var content = await ReadFileOrNullAsync(feed, key, stored.Version, manifest.FileName, path, cancellationToken);
        return content == null
            ? null
            : new VsCodeAsset { Content = new MemoryStream(content), ContentType = VsCodeArchiveReader.GetContentType(path) };
    }

    public async Task<VsCodeUploadResult> UploadAsync(Feed feed, Stream vsix, long maxBytes, CancellationToken cancellationToken)
    {
        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(vsix, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return new VsCodeUploadResult
            {
                Status = VsCodeUploadStatus.TooLarge,
                Message = $"The extension is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.",
            };
        }

        using (content)
        {
            VsCodeManifest manifest;
            try
            {
                manifest = VsCodeArchiveReader.Read(content.Stream);
            }
            catch (InvalidVsCodePackageException e)
            {
                LogInvalidPackage(e, feed.Slug);
                return new VsCodeUploadResult { Status = VsCodeUploadStatus.InvalidPackage, Message = e.Message };
            }

            var key = VsCodeExtensionId.Normalize(manifest.Id);
            using (await _lock.AcquireAsync(feed.Id, key, manifest.Version, cancellationToken))
            {
                var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, key, manifest.Version, cancellationToken);
                if (existing != null
                    && VsCodeVersionManifest.Parse(existing.MetadataJson).Find(manifest.TargetPlatform) != null
                    && !_feedSettings.CanOverwrite(feed, IsPreRelease(manifest)))
                {
                    return new VsCodeUploadResult
                    {
                        Status = VsCodeUploadStatus.AlreadyExists,
                        Id = manifest.Id,
                        Version = manifest.Version,
                        TargetPlatform = manifest.TargetPlatform,
                        Message = manifest.TargetPlatform == VsCodeTargetPlatform.Universal
                            ? $"Version {manifest.Version} of extension {manifest.Id} already exists."
                            : $"Version {manifest.Version} of extension {manifest.Id} for {manifest.TargetPlatform} already exists.",
                    };
                }

                await StoreAsync(feed, key, manifest, content, existing, cachedFrom: null, cancellationToken);
            }

            LogUploaded(manifest.Id, manifest.Version, manifest.TargetPlatform, feed.Slug);

            return new VsCodeUploadResult
            {
                Status = VsCodeUploadStatus.Success,
                Id = manifest.Id,
                Version = manifest.Version,
                TargetPlatform = manifest.TargetPlatform,
            };
        }
    }

    /// <summary>
    /// Searches the feed's extensions and then the mirror's: a page of the feed's extensions that
    /// match, followed by the mirror's that the feed doesn't have, as one list.
    /// </summary>
    private async Task<VsCodeGalleryQueryResult> SearchAsync(
        Feed feed,
        VsCodeGalleryQuery query,
        VsCodeGalleryQueryFilter filter,
        string galleryUrl,
        CancellationToken cancellationToken)
    {
        var criteria = filter.Criteria ?? [];
        var text = criteria.LastOrDefault(c => c.FilterType == VsCodeGalleryApi.SearchTextFilter)?.Value?.Trim();
        var tags = criteria.Where(c => c.FilterType == VsCodeGalleryApi.TagFilter).Select(c => c.Value).ToList();
        var categories = criteria.Where(c => c.FilterType == VsCodeGalleryApi.CategoryFilter).Select(c => c.Value).ToList();
        var pageSize = filter.PageSize > 0 ? Math.Min(filter.PageSize, MaxPageSize) : DefaultPageSize;
        var offset = (Math.Max(filter.PageNumber, 1) - 1) * pageSize;

        var local = (await _artifacts.SearchAsync(feed.Id, text, 0, int.MaxValue, cancellationToken)).Artifacts
            .Select(a => FromStored(a.Name, a.Versions, upstream: null, galleryUrl))
            .Where(e => e != null
                && tags.TrueForAll(t => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                && categories.TrueForAll(c => e.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        local = Sort(local, filter.SortBy);

        var page = local.Skip(offset).Take(pageSize).Select(e => FilterVersions(e, query.Flags)).ToList();
        var total = local.Count;

        // The mirror's extensions follow the feed's: the page starts at this offset of the mirror's list.
        var stored = local.Select(e => $"{e.Publisher.PublisherName}.{e.ExtensionName}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var upstreamOffset = Math.Max(0, offset - local.Count);
        var upstreamPage = upstreamOffset / pageSize + 1;
        var skip = upstreamOffset % pageSize;
        for (var i = 0; i < 2; i++)
        {
            var result = await _upstream.SearchAsync(feed, WithPage(query, filter, upstreamPage + i, pageSize), cancellationToken);
            if (result == null)
            {
                break;
            }

            if (i == 0)
            {
                total += result.GetTotalCount();
            }

            foreach (var extension in (result.Extensions ?? []).Skip(i == 0 ? skip : 0))
            {
                if (page.Count < pageSize
                    && extension.Publisher != null
                    && !stored.Contains($"{extension.Publisher.PublisherName}.{extension.ExtensionName}"))
                {
                    page.Add(FilterVersions(FromUpstream(extension, galleryUrl), query.Flags));
                }
            }

            if (page.Count >= pageSize || skip == 0 || (result.Extensions?.Count ?? 0) < pageSize)
            {
                break;
            }
        }

        return VsCodeGalleryQueryResult.Create(page, total);
    }

    /// <summary>
    /// The extension with every version of the feed and of the mirror, newest first. A version of
    /// the feed, listed or not, hides the mirror's version of the same number for that platform,
    /// and an unlisted version hides it for every platform.
    /// </summary>
    /// <returns>The extension, or null if it has no version.</returns>
    private async Task<VsCodeGalleryExtension> GetExtensionOrNullAsync(Feed feed, string id, string galleryUrl, CancellationToken cancellationToken)
    {
        if (!VsCodeExtensionId.TryParse(id, out _, out _))
        {
            return null;
        }

        var key = VsCodeExtensionId.Normalize(id);
        var stored = await _artifacts.FindVersionsAsync(feed.Id, key, cancellationToken);

        // The feed's own extensions are not looked up on the mirrors.
        var upstream = stored.Any(v => v.CachedFrom == null)
            ? null
            : await _upstream.FindAsync(feed, id, cancellationToken);

        return FromStored(key, stored, upstream?.Extension, galleryUrl)
            ?? (upstream == null ? null : FromUpstream(upstream.Extension, galleryUrl, stored));
    }

    /// <summary>
    /// The gallery entry of a stored extension, with the versions of its mirror that it doesn't have.
    /// </summary>
    /// <returns>The entry, or null if the extension has no listed version.</returns>
    private static VsCodeGalleryExtension FromStored(string key, IEnumerable<ArtifactVersion> versions, VsCodeGalleryExtension upstream, string galleryUrl)
    {
        var all = versions.ToList();
        var listed = all
            .Where(v => v.Listed)
            .Select(v => (Stored: v, Manifest: VsCodeVersionManifest.Parse(v.MetadataJson)))
            .Where(v => v.Manifest.Main != null)
            .OrderByDescending(v => v.Stored.Version, NpmVersion.StringComparer)
            .ToList();
        if (listed.Count == 0)
        {
            return null;
        }

        var main = listed[0].Manifest.Main;
        var galleryVersions = listed
            .SelectMany(v => v.Manifest.Files.Select(f => ToGalleryVersion(f, AsUtc(v.Stored.Published), galleryUrl, v.Stored.CachedFrom == null, upstream)))
            .ToList();

        if (upstream != null)
        {
            galleryVersions.AddRange(FromUpstream(upstream, galleryUrl, all).Versions);
        }

        var published = AsUtc(listed.Min(v => v.Stored.Published));
        var updated = AsUtc(listed.Max(v => v.Stored.Published));

        return new VsCodeGalleryExtension
        {
            // The mirror's UUID for an extension the mirror has, so VS Code can ask the mirror for it.
            ExtensionId = upstream?.ExtensionId is { } upstreamId && upstreamId != Guid.Empty ? upstreamId : VsCodeExtensionId.GetUuid("extension", key),
            ExtensionName = main.Name,
            DisplayName = main.DisplayName,
            ShortDescription = main.Description,
            Publisher = new VsCodeGalleryPublisher
            {
                PublisherId = VsCodeExtensionId.GetUuid("publisher", main.Publisher),
                PublisherName = main.Publisher,
                DisplayName = main.Publisher,
            },
            Versions = SortVersions(galleryVersions),
            Statistics = upstream?.Statistics ?? [new VsCodeGalleryStatistic { StatisticName = "install", Value = all.Sum(v => v.Downloads) }],
            Tags = main.Tags,
            Categories = main.Categories,
            ReleaseDate = upstream?.ReleaseDate ?? published,
            PublishedDate = upstream?.PublishedDate ?? published,
            LastUpdated = upstream == null || upstream.LastUpdated < updated ? updated : upstream.LastUpdated,
            Flags = main.GalleryFlags ?? "",
        };
    }

    /// <summary>
    /// A mirror's gallery entry with the URLs of its versions on this server, without the versions
    /// that <paramref name="stored"/> hides.
    /// </summary>
    private static VsCodeGalleryExtension FromUpstream(VsCodeGalleryExtension upstream, string galleryUrl, IReadOnlyList<ArtifactVersion> stored = null)
    {
        var publisher = upstream.Publisher.PublisherName;
        var id = $"{publisher}.{upstream.ExtensionName}";

        var hidden = (stored ?? [])
            .SelectMany(v => v.Listed
                ? VsCodeVersionManifest.Parse(v.MetadataJson).Files.Select(f => (v.Version, f.TargetPlatform))
                : [(v.Version, (string)null)])
            .ToList();

        var versions = new List<VsCodeGalleryVersion>();
        foreach (var version in upstream.Versions ?? [])
        {
            if (!NpmVersion.TryParse(version.Version, out _)
                || !VsCodeTargetPlatform.TryParse(version.TargetPlatform, out var platform)
                || hidden.Exists(h => string.Equals(h.Version, version.Version, StringComparison.OrdinalIgnoreCase)
                    && (h.TargetPlatform == null || h.TargetPlatform == platform)))
            {
                continue;
            }

            var assetUri = GetAssetUri(galleryUrl, publisher, upstream.ExtensionName, version.Version);
            versions.Add(new VsCodeGalleryVersion
            {
                Version = version.Version,
                TargetPlatform = platform,
                LastUpdated = version.LastUpdated,
                AssetUri = assetUri,
                FallbackAssetUri = assetUri,

                // The mirror's file is stored as it is, so its signature stays valid and is passed through.
                Files = (version.Files ?? [])
                    .Where(f => f.AssetType != null)
                    .Select(f => new VsCodeGalleryFile { AssetType = f.AssetType, Source = GetAssetSource(assetUri, f.AssetType, platform) })
                    .ToList(),
                Properties = version.Properties ?? [],
            });
        }

        return new VsCodeGalleryExtension
        {
            ExtensionId = upstream.ExtensionId != Guid.Empty ? upstream.ExtensionId : VsCodeExtensionId.GetUuid("extension", id),
            ExtensionName = upstream.ExtensionName,
            DisplayName = upstream.DisplayName,
            ShortDescription = upstream.ShortDescription,
            Publisher = new VsCodeGalleryPublisher
            {
                PublisherId = VsCodeExtensionId.GetUuid("publisher", publisher),
                PublisherName = publisher,
                DisplayName = upstream.Publisher.DisplayName ?? publisher,
            },
            Versions = SortVersions(versions),
            Statistics = upstream.Statistics ?? [],
            Tags = upstream.Tags ?? [],
            Categories = upstream.Categories ?? [],
            ReleaseDate = upstream.ReleaseDate,
            PublishedDate = upstream.PublishedDate,
            LastUpdated = upstream.LastUpdated,
            Flags = upstream.Flags ?? "",
        };
    }

    /// <summary>
    /// The gallery version of a stored file. A file uploaded to the feed is marked as a private
    /// extension, which VS Code installs without a signature, while it refuses unsigned public
    /// extensions from a gallery set in <c>product.json</c>. A file stored from the mirror keeps the
    /// mirror's signature.
    /// </summary>
    private static VsCodeGalleryVersion ToGalleryVersion(VsCodeManifest manifest, DateTime published, string galleryUrl, bool uploaded, VsCodeGalleryExtension upstream)
    {
        var assetUri = GetAssetUri(galleryUrl, manifest.Publisher, manifest.Name, manifest.Version);
        var mirrored = upstream?.Versions?.FirstOrDefault(v =>
            string.Equals(v.Version, manifest.Version, StringComparison.OrdinalIgnoreCase)
            && VsCodeTargetPlatform.TryParse(v.TargetPlatform, out var platform)
            && platform == manifest.TargetPlatform);
        var assetTypes = manifest.Assets.Keys
            .Append(VsCodeGalleryApi.VsixPackageAsset)
            .Append(VsCodeGalleryApi.VsixManifestAsset)
            .Concat(uploaded ? [] : (mirrored?.Files ?? []).Select(f => f.AssetType).Where(t => t != null))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new VsCodeGalleryVersion
        {
            Version = manifest.Version,
            TargetPlatform = manifest.TargetPlatform,
            LastUpdated = published,
            AssetUri = assetUri,
            FallbackAssetUri = assetUri,
            Files = assetTypes.Select(t => new VsCodeGalleryFile { AssetType = t, Source = GetAssetSource(assetUri, t, manifest.TargetPlatform) }).ToList(),
            Properties = uploaded
                ? [.. manifest.Properties, new VsCodeGalleryProperty { Key = VsCodeGalleryApi.PrivateProperty, Value = "true" }]
                : manifest.Properties,
        };
    }

    private static string GetAssetUri(string galleryUrl, string publisher, string name, string version)
    {
        return $"{galleryUrl}/asset/{Uri.EscapeDataString(publisher)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}";
    }

    private static string GetAssetSource(string assetUri, string assetType, string platform)
    {
        var source = $"{assetUri}/{Uri.EscapeDataString(assetType)}";
        return platform == VsCodeTargetPlatform.Universal ? source : $"{source}?targetPlatform={platform}";
    }

    /// <summary>
    /// Keeps the versions a query's flags ask for. VS Code asks for the latest stable and
    /// pre-release version, the latest version or all versions; each is chosen per platform.
    /// </summary>
    private static VsCodeGalleryExtension FilterVersions(VsCodeGalleryExtension extension, int flags)
    {
        if ((flags & VsCodeGalleryApi.IncludeLatestPrereleaseAndStableVersionOnlyFlag) != 0)
        {
            extension.Versions = extension.Versions
                .GroupBy(v => (v.TargetPlatform, IsPreRelease(v)))
                .Select(g => g.First())
                .ToList();
        }
        else if ((flags & VsCodeGalleryApi.IncludeLatestVersionOnlyFlag) != 0 || (flags & VsCodeGalleryApi.IncludeVersionsFlag) == 0)
        {
            extension.Versions = extension.Versions.GroupBy(v => v.TargetPlatform).Select(g => g.First()).ToList();
        }

        extension.Versions = SortVersions(extension.Versions);
        return extension;
    }

    private static List<VsCodeGalleryVersion> SortVersions(IEnumerable<VsCodeGalleryVersion> versions)
    {
        return versions
            .OrderByDescending(v => v.Version, NpmVersion.StringComparer)
            .ThenBy(v => v.TargetPlatform, StringComparer.Ordinal)
            .ToList();
    }

    private static List<VsCodeGalleryExtension> Sort(List<VsCodeGalleryExtension> extensions, int sortBy)
    {
        return sortBy switch
        {
            VsCodeGalleryApi.SortByLastUpdated => extensions.OrderByDescending(e => e.LastUpdated).ToList(),
            VsCodeGalleryApi.SortByTitle => extensions.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            VsCodeGalleryApi.SortByPublisherName => extensions.OrderBy(e => e.Publisher.PublisherName, StringComparer.OrdinalIgnoreCase).ToList(),
            VsCodeGalleryApi.SortByInstallCount => extensions.OrderByDescending(e => e.Statistics.FirstOrDefault(s => s.StatisticName == "install")?.Value ?? 0).ToList(),
            VsCodeGalleryApi.SortByPublishedDate => extensions.OrderByDescending(e => e.PublishedDate).ToList(),

            // Relevance: the order of the search, name matches first.
            _ => extensions,
        };
    }

    private static VsCodeGalleryQuery WithPage(VsCodeGalleryQuery query, VsCodeGalleryQueryFilter filter, int pageNumber, int pageSize)
    {
        return new VsCodeGalleryQuery
        {
            Filters =
            [
                new VsCodeGalleryQueryFilter
                {
                    Criteria = filter.Criteria ?? [],
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = filter.SortBy,
                    SortOrder = filter.SortOrder,
                },
            ],
            AssetTypes = query.AssetTypes ?? [],
            Flags = query.Flags,
        };
    }

    private static bool IsPreRelease(VsCodeGalleryVersion version)
    {
        var property = version.Properties?.FirstOrDefault(p => p.Key == VsCodeGalleryApi.PreReleaseProperty)?.Value;
        return string.Equals(property, "true", StringComparison.OrdinalIgnoreCase)
            || (NpmVersion.TryParse(version.Version, out var parsed) && parsed.IsPrerelease);
    }

    private static bool IsPreRelease(VsCodeManifest manifest)
    {
        return manifest.IsPreRelease || (NpmVersion.TryParse(manifest.Version, out var parsed) && parsed.IsPrerelease);
    }

    /// <summary>
    /// Passes through an asset of a version for a platform that only the mirror has.
    /// </summary>
    private async Task<VsCodeAsset> GetUpstreamAssetOrNullAsync(Feed feed, string id, string version, string platform, string assetType, CancellationToken cancellationToken)
    {
        var (upstream, upstreamVersion) = await FindUpstreamVersionAsync(feed, id, version, platform, cancellationToken);
        var url = upstreamVersion == null ? null : GetUpstreamUrl(upstreamVersion, assetType);

        return url == null ? null : await _upstream.GetFileOrNullAsync(feed, upstream.Mirror, url, cancellationToken);
    }

    /// <summary>
    /// The version of an extension for a platform on the feed's mirror, unless the feed has its own versions of the extension.
    /// </summary>
    private async Task<(VsCodeUpstreamExtension Extension, VsCodeGalleryVersion Version)> FindUpstreamVersionAsync(
        Feed feed,
        string id,
        string version,
        string platform,
        CancellationToken cancellationToken)
    {
        var stored = await _artifacts.FindVersionsAsync(feed.Id, VsCodeExtensionId.Normalize(id), cancellationToken);
        if (stored.Any(v => v.CachedFrom == null || (!v.Listed && string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase))))
        {
            return (null, null);
        }

        var upstream = await _upstream.FindAsync(feed, id, cancellationToken);
        var match = upstream?.Extension.Versions?.FirstOrDefault(v =>
            string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase)
            && VsCodeTargetPlatform.TryParse(v.TargetPlatform, out var p)
            && p == platform);

        return (upstream, match);
    }

    /// <summary>
    /// The URL of an asset of a mirror's version: the file it lists, or else the URL the gallery API builds.
    /// </summary>
    private static string GetUpstreamUrl(VsCodeGalleryVersion version, string assetType)
    {
        var file = version.Files?.FirstOrDefault(f => string.Equals(f.AssetType, assetType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(file?.Source))
        {
            return file.Source;
        }

        var baseUri = version.FallbackAssetUri ?? version.AssetUri;
        if (file == null || string.IsNullOrEmpty(baseUri))
        {
            return null;
        }

        var url = $"{baseUri}/{assetType}";
        return string.IsNullOrEmpty(version.TargetPlatform) ? url : $"{url}?targetPlatform={version.TargetPlatform}";
    }

    /// <summary>
    /// Stores the VSIX file of a version for a platform that the mirror offers, unless it is there already.
    /// </summary>
    /// <returns>The stored version and the manifest of its file, or nulls if no mirror has it.</returns>
    private async Task<(ArtifactVersion Version, VsCodeManifest Manifest)> MirrorAsync(Feed feed, string id, string version, string platform, CancellationToken cancellationToken)
    {
        var key = VsCodeExtensionId.Normalize(id);
        using var mirrorLock = await _lock.AcquireAsync(feed.Id, key, version, cancellationToken);

        // A concurrent request may have mirrored the file while we waited for the lock.
        var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, key, version, cancellationToken);
        var stored = existing == null ? null : VsCodeVersionManifest.Parse(existing.MetadataJson).Find(platform);
        if (stored != null)
        {
            return (existing, stored);
        }

        try
        {
            var (upstream, upstreamVersion) = await FindUpstreamVersionAsync(feed, id, version, platform, cancellationToken);
            var url = upstreamVersion == null ? null : GetUpstreamUrl(upstreamVersion, VsCodeGalleryApi.VsixPackageAsset);
            if (url == null)
            {
                return (null, null);
            }

            using var content = await _upstream.DownloadOrNullAsync(feed, upstream.Mirror, url, _feedSettings.GetMaxPackageSizeBytes(feed), cancellationToken);
            if (content == null)
            {
                return (null, null);
            }

            var manifest = VsCodeArchiveReader.Read(content.Stream);
            if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase)
                || manifest.TargetPlatform != platform)
            {
                LogMirroredPackageMismatch(url, id, version, platform, manifest.Id, manifest.Version, manifest.TargetPlatform);
                return (null, null);
            }

            if (!await StoreAsync(feed, key, manifest, content, existing, upstream.Mirror.PackageSource.AbsoluteUri, cancellationToken))
            {
                return (null, null);
            }

            LogMirrored(manifest.Id, version, platform, upstream.Mirror.PackageSource);

            var mirrored = await _artifacts.FindVersionOrNullAsync(feed.Id, key, version, cancellationToken);
            return (mirrored, mirrored == null ? null : VsCodeVersionManifest.Parse(mirrored.MetadataJson).Find(platform));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, id, version, platform);

            return (null, null);
        }
    }

    /// <summary>
    /// Stores a VSIX file: the file, the version with its manifest if the feed doesn't have it yet,
    /// or else the file's manifest in the version's manifest. The caller holds the version's lock.
    /// </summary>
    /// <returns>False if the version was deleted meanwhile.</returns>
    private async Task<bool> StoreAsync(
        Feed feed,
        string key,
        VsCodeManifest manifest,
        ArtifactFileContent content,
        ArtifactVersion existing,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;

        // A version matched regardless of case keeps the case it has.
        var version = existing?.Version ?? manifest.Version;
        manifest.FileName = VsCodeManifest.GetFileName(manifest.Id, version, manifest.TargetPlatform);

        content.Stream.Position = 0;
        await _storage.SaveFileAsync(feed.Slug, key, version, manifest.FileName, content.Stream, cancellationToken);

        if (existing == null)
        {
            content.Stream.Position = 0;
            var readme = manifest.Assets.TryGetValue(VsCodeGalleryApi.DetailsAsset, out var readmePath)
                ? VsCodeArchiveReader.ReadFileOrNull(content.Stream, readmePath)
                : null;
            if (readme != null)
            {
                using var readmeStream = new MemoryStream(readme);
                await _storage.SaveReadmeAsync(feed.Slug, key, version, readmeStream, cancellationToken);
            }

            var versionManifest = new VsCodeVersionManifest();
            versionManifest.AddOrReplace(manifest);

            var result = await _artifacts.AddVersionAsync(
                feed.Id,
                key,
                new ArtifactVersion
                {
                    Version = version,
                    IsPrerelease = IsPreRelease(manifest),
                    Listed = true,
                    Description = Truncate(manifest.Description),
                    MetadataJson = versionManifest.ToJson(),
                    HasReadme = readme != null,
                    CachedFrom = cachedFrom,
                    Published = now,
                },
                cancellationToken);

            // Another instance created the version meanwhile; the file's manifest is added to it below.
            existing = result == ArtifactAddResult.AlreadyExists
                ? await _artifacts.FindVersionOrNullAsync(feed.Id, key, version, cancellationToken)
                : null;
        }

        if (existing != null)
        {
            var versionManifest = VsCodeVersionManifest.Parse(existing.MetadataJson);
            versionManifest.AddOrReplace(manifest);

            if (!await _artifacts.UpdateManifestAsync(feed.Id, key, version, Truncate(versionManifest.Main.Description), versionManifest.ToJson(), cancellationToken))
            {
                return false;
            }
        }

        return await _artifacts.AddOrReplaceFileAsync(feed.Id, key, version, content.ToArtifactFile(manifest.FileName, now), cancellationToken);
    }

    /// <summary>
    /// Reads a file of a stored VSIX file, which a zip reader needs to seek.
    /// </summary>
    private async Task<byte[]> ReadFileOrNullAsync(Feed feed, string key, string version, string fileName, string path, CancellationToken cancellationToken)
    {
        await using var vsix = await _storage.GetFileStreamAsync(feed.Slug, key, version, fileName, cancellationToken);
        if (vsix == null)
        {
            return null;
        }

        if (vsix.CanSeek)
        {
            return VsCodeArchiveReader.ReadFileOrNull(vsix, path);
        }

        using var copy = await ArtifactFileContent.ReadAsync(vsix, long.MaxValue, cancellationToken);
        return VsCodeArchiveReader.ReadFileOrNull(copy.Stream, path);
    }

    /// <summary>
    /// A time the database returns, which is UTC whether or not it says so, so the editors read it as UTC.
    /// </summary>
    private static DateTime AsUtc(DateTime time)
    {
        return DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }

    private static string Truncate(string description)
    {
        var trimmed = description?.Trim();
        return trimmed?.Length > MaxDescriptionLength ? trimmed[..MaxDescriptionLength] : trimmed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An extension uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded extension {PackageName} {PackageVersion} for {TargetPlatform} to feed {FeedSlug}")]
    private partial void LogUploaded(string packageName, string packageVersion, string targetPlatform, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored extension {PackageName} {PackageVersion} for {TargetPlatform} from {PackageSource}")]
    private partial void LogMirrored(string packageName, string packageVersion, string targetPlatform, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file at {UpstreamUrl} for extension {PackageName} {PackageVersion} {TargetPlatform} is {FileName} {FileVersion} {FilePlatform}")]
    private partial void LogMirroredPackageMismatch(string upstreamUrl, string packageName, string packageVersion, string targetPlatform, string fileName, string fileVersion, string filePlatform);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror extension {PackageName} {PackageVersion} for {TargetPlatform}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion, string targetPlatform);
}
