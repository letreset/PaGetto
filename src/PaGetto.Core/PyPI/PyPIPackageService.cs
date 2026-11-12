using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.PyPI;

public partial class PyPIPackageService : IPyPIPackageService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    private const long BytesPerMiB = 1024L * 1024;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly PyPIUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<PyPIPackageService> _logger;

    public PyPIPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        PyPIUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<PyPIPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> ListProjectsAsync(Feed feed, CancellationToken cancellationToken)
    {
        return await _artifacts.FindNamesAsync(feed.Id, cancellationToken);
    }

    public async Task<PyPIProject> GetProjectOrNullAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        var versions = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);
        var stored = await _artifacts.FindFilesAsync(feed.Id, name, cancellationToken);

        // The metadata stored next to each wheel, by the wheel's name.
        var metadata = new Dictionary<string, ArtifactFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in stored.Where(f => IsMetadataFile(f.FileName)))
        {
            metadata.TryAdd(file.FileName[..^PyPIFileName.MetadataSuffix.Length], file);
        }

        var requiresPython = versions.ToDictionary(v => v.Key, v => GetRequiresPython(v.MetadataJson));

        var files = new List<PyPIProjectFile>();
        foreach (var file in stored.Where(f => !IsMetadataFile(f.FileName)))
        {
            files.Add(new PyPIProjectFile
            {
                FileName = file.FileName,
                Version = file.ArtifactVersion.Version,
                Sha256 = file.Sha256,
                Size = file.Size,
                RequiresPython = requiresPython.GetValueOrDefault(file.ArtifactVersionKey),
                Yanked = !file.ArtifactVersion.Listed,
                CoreMetadataSha256 = metadata.GetValueOrDefault(file.FileName)?.Sha256,
                UploadTime = file.CreatedAtUtc,
            });
        }

        // A version published to the feed hides the same version of the mirror. Of a version that
        // was mirrored, the files that were not downloaded yet are still listed.
        var byVersion = versions.ToDictionary(v => v.Version, StringComparer.OrdinalIgnoreCase);
        var storedNames = stored.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in await _upstream.GetFilesAsync(feed, name, cancellationToken))
        {
            byVersion.TryGetValue(file.Version, out var local);
            if ((local != null && local.CachedFrom == null) || storedNames.Contains(file.FileName))
            {
                continue;
            }

            files.Add(new PyPIProjectFile
            {
                FileName = file.FileName,
                Version = file.Version,
                Sha256 = file.Sha256,
                Size = file.Size,
                RequiresPython = file.RequiresPython,

                // Unlisting a mirrored version in the feed yanks all of its files.
                Yanked = local != null ? !local.Listed : file.Yanked,
                YankedReason = local != null ? null : file.YankedReason,
                UploadTime = file.UploadTime,
            });
        }

        if (files.Count == 0)
        {
            return null;
        }

        files = files
            .OrderBy(f => f.Version, PyPIVersion.StringComparer)
            .ThenBy(f => f.FileName, StringComparer.Ordinal)
            .ToList();

        return new PyPIProject
        {
            Name = name,
            Versions = files.Select(f => f.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Files = files,
        };
    }

    public async Task<PyPIDownload> DownloadOrNullAsync(Feed feed, string name, string version, string fileName, bool countDownload, CancellationToken cancellationToken)
    {
        version = PyPIVersion.NormalizeOrNull(version);
        if (version == null || string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var file = await FindStoredFileAsync(feed, name, version, fileName, cancellationToken);
        if (file == null && !IsMetadataFile(fileName))
        {
            var local = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
            if (local == null || local.CachedFrom != null)
            {
                file = await MirrorAsync(feed, name, version, fileName, cancellationToken);
            }
        }

        if (file == null)
        {
            return null;
        }

        var stream = await _storage.GetFileStreamAsync(feed.Slug, name, version, file.FileName, cancellationToken);
        if (stream == null)
        {
            LogStoredFileMissing(file.FileName, name, version, feed.Slug);
            return null;
        }

        if (countDownload && !IsMetadataFile(file.FileName))
        {
            await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);
        }

        return new PyPIDownload { Stream = stream, File = file };
    }

    public async Task<PyPIUploadResult> UploadAsync(Feed feed, PyPIUpload upload, long maxBytes, CancellationToken cancellationToken)
    {
        if (!PyPIPackageName.IsValid(upload.Name))
        {
            return Invalid(null, null, $"'{upload.Name}' is not a valid project name.");
        }

        var name = PyPIPackageName.Normalize(upload.Name);
        if (!PyPIFileName.TryParse(upload.FileName, upload.Name, out var fileName))
        {
            return Invalid(name, null, $"'{upload.FileName}' is not the name of a wheel or an sdist (.tar.gz or .zip) of {upload.Name} with a PEP 440 version.");
        }

        var version = fileName.Version;
        if (!string.IsNullOrEmpty(upload.Version) && PyPIVersion.NormalizeOrNull(upload.Version) != version)
        {
            return Invalid(name, version, $"{fileName.FileName} is a file of version {version}, but it was uploaded for version {upload.Version}.");
        }

        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(upload.Content, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException e)
        {
            return new PyPIUploadResult { Status = PyPIUploadStatus.TooLarge, Name = name, Version = version, Message = e.Message };
        }

        using (content)
        {
            if (!MatchesDigest(upload.Sha256Digest, content.Sha256) || !MatchesDigest(upload.Md5Digest, content.Md5))
            {
                return Invalid(name, version, $"{fileName.FileName} does not match the digest that was uploaded with it.");
            }

            PyPIArchive archive;
            try
            {
                archive = await PyPIArchiveReader.ReadAsync(content.Stream, fileName, upload.Name, cancellationToken);
            }
            catch (InvalidPyPIPackageException e)
            {
                LogUploadRejected(e, fileName.FileName, feed.Slug);
                return Invalid(name, version, e.Message);
            }

            var existing = await FindStoredFileAsync(feed, name, version, fileName.FileName, cancellationToken);
            if (existing != null)
            {
                // Clients retry uploads, and the same file again changes nothing.
                if (string.Equals(existing.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new PyPIUploadResult { Status = PyPIUploadStatus.Success, Name = name, Version = version };
                }

                if (!CanOverwrite(feed, version))
                {
                    return new PyPIUploadResult
                    {
                        Status = PyPIUploadStatus.Conflict,
                        Name = name,
                        Version = version,

                        // twine --skip-existing looks for this text.
                        Message = $"File already exists: {fileName.FileName} of {name} {version}, and the feed does not allow overwriting it.",
                    };
                }
            }

            var now = _time.UtcNow;
            await EnsureVersionAsync(feed, name, version, archive, cachedFrom: null, published: now, listed: true, cancellationToken);

            if (await StoreAsync(feed, name, fileName, content, archive, now, cancellationToken) == null)
            {
                return new PyPIUploadResult
                {
                    Status = PyPIUploadStatus.Conflict,
                    Name = name,
                    Version = version,
                    Message = "The version was deleted while the file was uploaded.",
                };
            }

            LogPublished(fileName.FileName, name, version, feed.Slug);

            return new PyPIUploadResult { Status = PyPIUploadStatus.Success, Name = name, Version = version };
        }
    }

    /// <summary>
    /// Stores a file of a version from the feed's mirror in the feed.
    /// </summary>
    /// <returns>The stored file, or null if the mirror doesn't have it or it could not be stored.</returns>
    private async Task<ArtifactFile> MirrorAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetMirrorOptions(feed).Count == 0)
        {
            return null;
        }

        var upstreamFile = (await _upstream.GetFilesAsync(feed, name, cancellationToken))
            .FirstOrDefault(f => f.Version == version && string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (upstreamFile == null || !PyPIFileName.TryParse(upstreamFile.FileName, name, out var parsed))
        {
            return null;
        }

        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, $"{version}/{parsed.FileName}", cancellationToken);

        // A concurrent request may have stored the file while we waited for the lock.
        var stored = await FindStoredFileAsync(feed, name, version, parsed.FileName, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            using var content = await _upstream.DownloadFileOrNullAsync(feed, upstreamFile, GetMaxBytes(feed), cancellationToken);
            if (content == null)
            {
                return null;
            }

            // The metadata is only needed for the web UI and PEP 658, so a mirrored file without
            // readable metadata is still served.
            PyPIArchive archive = null;
            try
            {
                archive = await PyPIArchiveReader.ReadAsync(content.Stream, parsed, name, cancellationToken);
            }
            catch (InvalidPyPIPackageException e)
            {
                LogMirroredMetadataUnreadable(e, upstreamFile.Url);
            }

            // The mirror's upload time, which installers can filter on (uv --exclude-newer).
            var uploaded = upstreamFile.UploadTime ?? _time.UtcNow;
            await EnsureVersionAsync(
                feed,
                name,
                version,
                archive,
                cachedFrom: upstreamFile.Mirror.PackageSource.AbsoluteUri,
                published: uploaded,
                listed: !upstreamFile.Yanked,
                cancellationToken);

            var file = await StoreAsync(feed, name, parsed, content, archive, uploaded, cancellationToken);
            if (file != null)
            {
                LogMirrored(upstreamFile.Url, upstreamFile.Mirror.PackageSource);
            }

            return file;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, upstreamFile.Url);

            return null;
        }
    }

    /// <summary>
    /// Creates a version with the metadata of its first file, unless the feed has it already.
    /// </summary>
    private async Task EnsureVersionAsync(
        Feed feed,
        string name,
        string version,
        PyPIArchive archive,
        string cachedFrom,
        DateTime published,
        bool listed,
        CancellationToken cancellationToken)
    {
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) != null)
        {
            return;
        }

        var manifest = archive?.Manifest ?? PyPICoreMetadata.CreateManifest(name, version);

        if (archive?.Readme != null)
        {
            using var readme = new MemoryStream(Encoding.UTF8.GetBytes(archive.Readme));
            await _storage.SaveReadmeAsync(feed.Slug, name, version, readme, cancellationToken);
        }

        var summary = PyPICoreMetadata.GetString(manifest, "summary");

        // AlreadyExists means a concurrent upload of another file of the version created it.
        await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = version,
                IsPrerelease = PyPIVersion.Parse(version).IsPrerelease,
                Listed = listed,
                Description = summary?.Length > MaxDescriptionLength ? summary[..MaxDescriptionLength] : summary,
                MetadataJson = manifest.ToJsonString(),
                HasReadme = archive?.Readme != null,
                CachedFrom = cachedFrom,
                Published = published,
            },
            cancellationToken);
    }

    /// <summary>
    /// Stores a file of a version that exists, and the core metadata of a wheel next to it.
    /// </summary>
    /// <returns>The stored file, or null if the version was deleted meanwhile.</returns>
    private async Task<ArtifactFile> StoreAsync(
        Feed feed,
        string name,
        PyPIFileName fileName,
        ArtifactFileContent content,
        PyPIArchive archive,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        var version = fileName.Version;

        content.Stream.Position = 0;
        await _storage.SaveFileAsync(feed.Slug, name, version, fileName.FileName, content.Stream, cancellationToken);

        var file = content.ToArtifactFile(fileName.FileName, createdAtUtc);
        if (!await _artifacts.AddOrReplaceFileAsync(feed.Id, name, version, file, cancellationToken))
        {
            return null;
        }

        if (fileName.IsWheel && archive != null)
        {
            var metadataFileName = fileName.FileName + PyPIFileName.MetadataSuffix;

            using var metadata = await ArtifactFileContent.ReadAsync(new MemoryStream(archive.Metadata), long.MaxValue, cancellationToken);
            await _storage.SaveFileAsync(feed.Slug, name, version, metadataFileName, metadata.Stream, cancellationToken);
            await _artifacts.AddOrReplaceFileAsync(feed.Id, name, version, metadata.ToArtifactFile(metadataFileName, createdAtUtc), cancellationToken);
        }

        return file;
    }

    private async Task<ArtifactFile> FindStoredFileAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        var files = await _artifacts.FindFilesAsync(feed.Id, name, version, cancellationToken);

        return files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanOverwrite(Feed feed, string version)
    {
        return _feedSettings.GetAllowPackageOverwrites(feed) switch
        {
            PackageOverwriteAllowed.True => true,
            PackageOverwriteAllowed.PrereleaseOnly => PyPIVersion.Parse(version).IsPrerelease,
            _ => false,
        };
    }

    private long GetMaxBytes(Feed feed)
    {
        return _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
    }

    /// <summary>
    /// Whether a stored file is the core metadata of a wheel rather than a distribution.
    /// </summary>
    public static bool IsMetadataFile(string fileName)
    {
        return fileName.EndsWith(PyPIFileName.MetadataSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDigest(string uploaded, string computed)
    {
        return string.IsNullOrWhiteSpace(uploaded) || string.Equals(uploaded.Trim(), computed, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRequiresPython(string manifestJson)
    {
        try
        {
            return PyPICoreMetadata.GetString(JsonNode.Parse(manifestJson ?? "null") as JsonObject, "requires_python");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PyPIUploadResult Invalid(string name, string version, string message)
    {
        return new PyPIUploadResult { Status = PyPIUploadStatus.Invalid, Name = name, Version = version, Message = message };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Python file {FileName} of {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string fileName, string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Python file {FileName} uploaded to feed {FeedSlug} is invalid")]
    private partial void LogUploadRejected(Exception exception, string fileName, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored Python file {Url} from {PackageSource}")]
    private partial void LogMirrored(string url, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The metadata of the mirrored file {Url} could not be read, storing it without")]
    private partial void LogMirroredMetadataUnreadable(Exception exception, string url);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror Python file {Url}")]
    private partial void LogMirrorFailed(Exception exception, string url);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored Python file {FileName} of {PackageName} {PackageVersion} in feed {FeedSlug} is missing from storage")]
    private partial void LogStoredFileMissing(string fileName, string packageName, string packageVersion, string feedSlug);
}
