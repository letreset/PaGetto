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
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Vsix;

public partial class VsixPackageService : IVsixPackageService
{
    private const long BytesPerMiB = 1024L * 1024;
    private const int MaxDescriptionLength = 4000;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly VsixUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<VsixPackageService> _logger;

    public VsixPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        VsixUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<VsixPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetAtomFeedAsync(Feed feed, string galleryUrl, CancellationToken cancellationToken)
    {
        var artifacts = (await _artifacts.SearchAsync(feed.Id, query: null, skip: 0, take: int.MaxValue, cancellationToken)).Artifacts;
        var upstream = await _upstream.GetIndexAsync(feed, cancellationToken);

        var entries = new List<VsixGalleryEntry>();
        foreach (var artifact in artifacts)
        {
            var entry = FindCurrent(artifact, upstream.GetValueOrDefault(artifact.Name), galleryUrl);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        var stored = artifacts.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, entry) in upstream)
        {
            if (!stored.Contains(key))
            {
                entries.Add(FromUpstream(entry, galleryUrl));
            }
        }

        var updated = entries.Count == 0 ? feed.UpdatedAtUtc : entries.Max(e => e.Updated);

        return VsixAtomFeed.Write(
            $"uuid:{feed.Id}",
            feed.Name,
            $"{galleryUrl}/{VsixGalleryPaths.AtomFeed}",
            updated,
            entries.OrderBy(e => e.Manifest.Id, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<Stream> GetPackageStreamOrNullAsync(Feed feed, string id, string version, CancellationToken cancellationToken)
    {
        if (!VsixExtensionId.IsValid(id) || !VsixVersion.TryParse(version, out var parsed))
        {
            return null;
        }

        var key = VsixExtensionId.Normalize(id);
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, key, parsed.Text, cancellationToken)
            ?? await MirrorAsync(feed, key, parsed, cancellationToken);
        if (stored == null)
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, key, stored.Version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, key, stored.Version, cancellationToken);
    }

    public async Task<VsixImage> GetImageOrNullAsync(Feed feed, string id, string version, VsixImageKind kind, CancellationToken cancellationToken)
    {
        if (!VsixExtensionId.IsValid(id) || !VsixVersion.TryParse(version, out var parsed))
        {
            return null;
        }

        var key = VsixExtensionId.Normalize(id);
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, key, parsed.Text, cancellationToken);
        if (stored == null)
        {
            var upstream = (await _upstream.GetIndexAsync(feed, cancellationToken)).GetValueOrDefault(key);
            return upstream?.Version.Text == parsed.Text
                ? await _upstream.GetImageOrNullAsync(feed, upstream, kind, cancellationToken)
                : null;
        }

        var manifest = VsixManifest.Parse(stored.MetadataJson);
        var path = kind == VsixImageKind.Icon ? manifest.Icon : manifest.PreviewImage;
        if (path == null)
        {
            return null;
        }

        // The images are read from the stored file, which a zip reader needs to seek.
        await using var archive = await _storage.GetArchiveStreamAsync(feed.Slug, key, stored.Version, cancellationToken);
        if (archive.CanSeek)
        {
            return VsixArchiveReader.ReadImage(archive, path);
        }

        using var copy = await ArtifactFileContent.ReadAsync(archive, long.MaxValue, cancellationToken);
        return VsixArchiveReader.ReadImage(copy.Stream, path);
    }

    public async Task<VsixUploadResult> UploadAsync(Feed feed, Stream vsix, long maxBytes, CancellationToken cancellationToken)
    {
        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(vsix, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException)
        {
            return new VsixUploadResult
            {
                Status = VsixUploadStatus.TooLarge,
                Message = $"The extension is larger than the feed's limit of {maxBytes / BytesPerMiB} MiB.",
            };
        }

        VsixManifest manifest;
        using (content)
        {
            try
            {
                manifest = VsixArchiveReader.Read(content.Stream);
            }
            catch (InvalidVsixPackageException e)
            {
                LogInvalidPackage(e, feed.Slug);
                return new VsixUploadResult { Status = VsixUploadStatus.InvalidPackage, Message = e.Message };
            }

            var key = VsixExtensionId.Normalize(manifest.Id);
            var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, key, manifest.Version, cancellationToken);
            if (existing != null)
            {
                // Extensions have no prereleases, so only a feed that allows every overwrite replaces a version.
                if (!_feedSettings.CanOverwrite(feed, isPrerelease: false))
                {
                    return AlreadyExists(manifest);
                }

                await _artifacts.HardDeleteVersionAsync(feed.Id, key, existing.Version, cancellationToken);
                await _storage.DeleteAsync(feed.Slug, key, existing.Version, cancellationToken);
            }

            if (await IndexAsync(feed, key, manifest, content, cachedFrom: null, cancellationToken) == ArtifactAddResult.AlreadyExists)
            {
                return AlreadyExists(manifest);
            }
        }

        LogUploaded(manifest.Id, manifest.Version, feed.Slug);

        return new VsixUploadResult { Status = VsixUploadStatus.Success, Id = manifest.Id, Version = manifest.Version };
    }

    /// <summary>
    /// The entry of an extension of the feed: its newest listed version, or the mirror's version
    /// when it is newer and the feed has no version of that number.
    /// </summary>
    /// <returns>The entry, or null when the extension has no listed version and the mirror has none to offer.</returns>
    private static VsixGalleryEntry FindCurrent(Artifact artifact, VsixUpstreamEntry upstream, string galleryUrl)
    {
        var versions = artifact.Versions.ToList();

        var newest = versions
            .Where(v => v.Listed)
            .Select(v => (Stored: v, Parsed: VsixVersion.TryParse(v.Version, out var parsed) ? parsed : null))
            .Where(v => v.Parsed != null)
            .OrderByDescending(v => v.Parsed)
            .FirstOrDefault();

        var hidden = upstream == null || versions.Exists(v => v.Version == upstream.Version.Text);
        if (!hidden && (newest.Stored == null || upstream.Version.CompareTo(newest.Parsed) > 0))
        {
            return FromUpstream(upstream, galleryUrl);
        }

        if (newest.Stored == null)
        {
            return null;
        }

        var manifest = VsixManifest.Parse(newest.Stored.MetadataJson);
        var id = manifest.Id ?? artifact.Name;
        var version = newest.Stored.Version;

        return new VsixGalleryEntry
        {
            Manifest = manifest,
            Published = newest.Stored.Published,
            Updated = newest.Stored.Published,
            Downloads = versions.Sum(v => v.Downloads),
            DownloadUrl = $"{galleryUrl}/{VsixGalleryPaths.GetFilePath(id, version)}",
            IconUrl = manifest.Icon == null ? null : $"{galleryUrl}/{VsixGalleryPaths.GetImagePath(id, version, VsixImageKind.Icon)}",
            PreviewImageUrl = manifest.PreviewImage == null ? null : $"{galleryUrl}/{VsixGalleryPaths.GetImagePath(id, version, VsixImageKind.PreviewImage)}",
        };
    }

    /// <summary>
    /// The entry of an extension that a mirror offers, linked on this server: the file is stored
    /// on its first download, and the images are passed through.
    /// </summary>
    private static VsixGalleryEntry FromUpstream(VsixUpstreamEntry upstream, string galleryUrl)
    {
        var entry = upstream.Entry;
        var id = entry.Manifest.Id;
        var version = upstream.Version.Text;

        return new VsixGalleryEntry
        {
            Manifest = entry.Manifest,
            Published = entry.Published,
            Updated = entry.Updated,
            Downloads = entry.Downloads,
            DownloadUrl = $"{galleryUrl}/{VsixGalleryPaths.GetFilePath(id, version)}",
            IconUrl = entry.IconUrl == null ? null : $"{galleryUrl}/{VsixGalleryPaths.GetImagePath(id, version, VsixImageKind.Icon)}",
            PreviewImageUrl = entry.PreviewImageUrl == null ? null : $"{galleryUrl}/{VsixGalleryPaths.GetImagePath(id, version, VsixImageKind.PreviewImage)}",
        };
    }

    /// <summary>
    /// Stores the version a mirror offers in the feed, unless it is there already.
    /// </summary>
    /// <returns>The stored version, or null if no mirror has it.</returns>
    private async Task<ArtifactVersion> MirrorAsync(Feed feed, string key, VsixVersion version, CancellationToken cancellationToken)
    {
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, key, version.Text, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, key, version.Text, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var upstream = (await _upstream.GetIndexAsync(feed, cancellationToken)).GetValueOrDefault(key);
            if (upstream == null || upstream.Version.Text != version.Text)
            {
                return null;
            }

            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            using var content = await _upstream.DownloadOrNullAsync(feed, upstream, maxBytes, cancellationToken);
            if (content == null)
            {
                return null;
            }

            // The manifest has more than the gallery entry. A file this server can't read is
            // still served, with what the entry has; a file of another extension is not.
            VsixManifest manifest;
            try
            {
                manifest = VsixArchiveReader.Read(content.Stream);
            }
            catch (InvalidVsixPackageException e)
            {
                LogMirroredPackageUnreadable(e, upstream.Entry.Manifest.Id, version.Text);
                manifest = upstream.Entry.Manifest;
            }

            if (VsixExtensionId.Normalize(manifest.Id) != key || manifest.Version != version.Text)
            {
                LogMirroredPackageMismatch(upstream.Entry.DownloadUrl, upstream.Entry.Manifest.Id, version.Text, manifest.Id, manifest.Version);
                return null;
            }

            var result = await IndexAsync(feed, key, manifest, content, upstream.Mirror.PackageSource.AbsoluteUri, cancellationToken);

            LogMirrored(manifest.Id, version.Text, upstream.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return await _artifacts.FindVersionOrNullAsync(feed.Id, key, version.Text, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, key, version.Text);

            return null;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string key,
        VsixManifest manifest,
        ArtifactFileContent vsix,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        vsix.Stream.Position = 0;
        await _storage.SaveAsync(feed.Slug, key, manifest.Version, vsix.Stream, readme: null, cancellationToken);

        var description = manifest.Description?.Trim();

        return await _artifacts.AddVersionAsync(
            feed.Id,
            key,
            new ArtifactVersion
            {
                Version = manifest.Version,
                IsPrerelease = false,
                Listed = true,
                Description = description?.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                MetadataJson = manifest.ToJson(),
                ContentHash = vsix.Sha256,
                Size = vsix.Size,
                HasReadme = false,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            cancellationToken);
    }

    private static VsixUploadResult AlreadyExists(VsixManifest manifest)
    {
        return new VsixUploadResult
        {
            Status = VsixUploadStatus.AlreadyExists,
            Id = manifest.Id,
            Version = manifest.Version,
            Message = $"Version {manifest.Version} of extension {manifest.Id} already exists.",
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An extension uploaded to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded extension {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogUploaded(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored extension {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored extension {PackageName} {PackageVersion} could not be read, storing it with what the mirror's gallery has")]
    private partial void LogMirroredPackageUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file at {UpstreamUrl} for extension {PackageName} {PackageVersion} is {FileName} {FileVersion}")]
    private partial void LogMirroredPackageMismatch(string upstreamUrl, string packageName, string packageVersion, string fileName, string fileVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror extension {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
