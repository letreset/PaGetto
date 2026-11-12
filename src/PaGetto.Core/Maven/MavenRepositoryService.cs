using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

namespace PaGetto.Core.Maven;

public partial class MavenRepositoryService : IMavenRepositoryService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    // What an uploaded checksum or metadata file may have; both are small.
    private const long MaxChecksumBytes = 1024;
    private const long MaxMetadataBytes = 16 * 1024 * 1024;

    private const long BytesPerMiB = 1024L * 1024;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly MavenUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<MavenRepositoryService> _logger;

    public MavenRepositoryService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        MavenUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<MavenRepositoryService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MavenContent> GetAsync(Feed feed, MavenPath path, bool countDownload, CancellationToken cancellationToken)
    {
        if (path.Kind == MavenPathKind.File)
        {
            return await GetFileOrNullAsync(feed, path, countDownload, cancellationToken);
        }

        var metadata = path.Kind == MavenPathKind.SnapshotMetadata
            ? await GetSnapshotMetadataOrNullAsync(feed, path, cancellationToken)
            : await GetArtifactMetadataOrNullAsync(feed, path, cancellationToken);

        if (metadata == null)
        {
            return null;
        }

        return path.Checksum is { } checksum
            ? Text(ComputeChecksum(metadata, checksum))
            : new MavenContent { Stream = new MemoryStream(metadata), ContentType = "application/xml" };
    }

    public async Task<MavenPutResult> PutAsync(Feed feed, MavenPath path, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        if (path.Kind != MavenPathKind.File)
        {
            // Clients upload the metadata they computed; the server writes its own instead.
            try
            {
                await new LimitedReadStream(content, MaxMetadataBytes, () => new InvalidDataException())
                    .CopyToAsync(Stream.Null, cancellationToken);
            }
            catch (InvalidDataException)
            {
                return Invalid("The metadata file is too large.");
            }

            return MavenPutResult.Success;
        }

        if (path.Checksum is { } checksum)
        {
            return await CheckUploadedChecksumAsync(feed, path, checksum, content, cancellationToken);
        }

        MavenFileContent file;
        try
        {
            file = await MavenFileContent.ReadAsync(content, maxBytes, cancellationToken);
        }
        catch (MavenFileTooLargeException e)
        {
            return new MavenPutResult { Status = MavenPutStatus.TooLarge, Message = e.Message };
        }

        using (file)
        {
            JsonObject manifest = null;
            if (path.File.IsPom)
            {
                try
                {
                    manifest = MavenPomReader.Read(file.Stream);
                    CheckCoordinates(manifest, path);
                }
                catch (InvalidMavenPomException e)
                {
                    LogPomRejected(e, path.FilePath, feed.Slug);
                    return Invalid(e.Message);
                }

                file.Stream.Position = 0;
            }

            var name = path.PackageName;
            var existing = await FindStoredFileAsync(feed, name, path.Version, path.FileName, cancellationToken);
            if (existing != null)
            {
                // Clients retry uploads, and the same content again changes nothing.
                if (string.Equals(existing.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new MavenPutResult { Status = MavenPutStatus.Success, StoredFile = true };
                }

                if (!CanOverwrite(feed, path.Version))
                {
                    return new MavenPutResult
                    {
                        Status = MavenPutStatus.Conflict,
                        Message = $"{path.FileName} already exists in {path.GroupId}:{path.ArtifactId} {path.Version}, and the feed does not allow overwriting it.",
                    };
                }
            }

            var now = _time.UtcNow;
            await EnsureVersionAsync(feed, path, manifest, cachedFrom: null, published: now, cancellationToken);

            await _storage.SaveFileAsync(feed.Slug, name, path.Version, path.FileName, file.Stream, cancellationToken);
            if (!await _artifacts.AddOrReplaceFileAsync(feed.Id, name, path.Version, file.ToArtifactFile(path.FileName, now), cancellationToken))
            {
                return new MavenPutResult { Status = MavenPutStatus.Conflict, Message = "The version was deleted while the file was uploaded." };
            }

            if (manifest != null)
            {
                await UpdateManifestAsync(feed, name, path.Version, manifest, cancellationToken);
            }

            LogPublished(path.FileName, name, path.Version, feed.Slug);

            return new MavenPutResult { Status = MavenPutStatus.Success, StoredFile = true };
        }
    }

    private async Task<MavenContent> GetFileOrNullAsync(Feed feed, MavenPath path, bool countDownload, CancellationToken cancellationToken)
    {
        var name = path.PackageName;
        var version = await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Version, cancellationToken);
        var file = version == null ? null : await FindStoredFileAsync(feed, name, path.Version, path.FileName, cancellationToken);

        // A version published to the feed hides the same version of a mirror. Snapshots change with
        // every build, so they are not mirrored.
        if (file == null && (version == null || version.CachedFrom != null) && !MavenVersion.IsSnapshot(path.Version))
        {
            file = await MirrorAsync(feed, path, cancellationToken);
            version ??= await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Version, cancellationToken);
        }

        if (file == null || version == null)
        {
            return null;
        }

        if (path.Checksum is { } checksum)
        {
            return Text(MavenChecksums.GetValue(file, checksum));
        }

        var stream = await _storage.GetFileStreamAsync(feed.Slug, name, path.Version, file.FileName, cancellationToken);
        if (stream == null)
        {
            LogStoredFileMissing(file.FileName, name, path.Version, feed.Slug);
            return null;
        }

        if (countDownload && IsMainFile(version, path.File))
        {
            await _artifacts.AddDownloadAsync(feed.Id, name, path.Version, cancellationToken);
        }

        return new MavenContent { Stream = stream, ContentType = GetContentType(path.File), File = file };
    }

    /// <summary>
    /// Writes the metadata of an artifact from the feed's listed versions and its mirrors' versions.
    /// </summary>
    private async Task<byte[]> GetArtifactMetadataOrNullAsync(Feed feed, MavenPath path, CancellationToken cancellationToken)
    {
        var local = (await _artifacts.FindVersionsAsync(feed.Id, path.PackageName, cancellationToken))
            .Where(v => v.Listed)
            .ToList();

        var upstream = await _upstream.GetMetadataOrNullAsync(feed, path.FilePath, cancellationToken);

        // The feed's own spelling of a version comes first, and wins.
        var versions = local.Select(v => v.Version)
            .Concat(upstream?.Versions ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(MavenVersion.Parse)
            .Order()
            .Select(v => v.Value)
            .ToList();

        var plugins = upstream?.Plugins ?? [];
        if (versions.Count == 0 && plugins.Count == 0)
        {
            return null;
        }

        var lastUpdated = local.Select(v => v.Published)
            .Append(upstream?.LastUpdated ?? DateTime.UnixEpoch)
            .Max();

        return MavenMetadataXml.WriteArtifact(path.GroupId, path.ArtifactId, versions, lastUpdated, plugins);
    }

    /// <summary>
    /// Writes the metadata of a snapshot version from its stored files.
    /// </summary>
    private async Task<byte[]> GetSnapshotMetadataOrNullAsync(Feed feed, MavenPath path, CancellationToken cancellationToken)
    {
        var files = new List<(MavenFileName, DateTime)>();
        foreach (var file in await _artifacts.FindFilesAsync(feed.Id, path.PackageName, path.Version, cancellationToken))
        {
            if (MavenFileName.TryParse(path.ArtifactId, path.Version, file.FileName, out var parsed))
            {
                files.Add((parsed, file.CreatedAtUtc));
            }
        }

        return files.Count == 0
            ? null
            : MavenMetadataXml.WriteSnapshot(path.GroupId, path.ArtifactId, path.Version, files);
    }

    /// <summary>
    /// Stores a file of a release version from the feed's mirrors in the feed.
    /// </summary>
    /// <returns>The stored file, or null if no mirror has it or it could not be stored.</returns>
    private async Task<ArtifactFile> MirrorAsync(Feed feed, MavenPath path, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetMirrorOptions(feed).Count == 0)
        {
            return null;
        }

        var name = path.PackageName;
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, $"{path.Version}/{path.FileName}", cancellationToken);

        // A concurrent request may have stored the file while we waited for the lock.
        var stored = await FindStoredFileAsync(feed, name, path.Version, path.FileName, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            using var downloaded = await _upstream.DownloadFileOrNullAsync(feed, path.FilePath, GetMaxBytes(feed), cancellationToken);
            if (downloaded == null)
            {
                return null;
            }

            var content = downloaded.Content;

            // The POM is only needed for the web UI, so a mirrored one that can't be read is still served.
            JsonObject manifest = null;
            if (path.File.IsPom)
            {
                try
                {
                    manifest = MavenPomReader.Read(content.Stream);
                }
                catch (InvalidMavenPomException e)
                {
                    LogMirroredPomUnreadable(e, path.FilePath);
                }

                content.Stream.Position = 0;
            }

            var now = _time.UtcNow;
            var source = downloaded.Mirror.PackageSource.AbsoluteUri;
            await EnsureVersionAsync(feed, path, manifest, source, downloaded.LastModified ?? now, cancellationToken);

            var file = content.ToArtifactFile(path.FileName, now);
            await _storage.SaveFileAsync(feed.Slug, name, path.Version, path.FileName, content.Stream, cancellationToken);
            if (!await _artifacts.AddOrReplaceFileAsync(feed.Id, name, path.Version, file, cancellationToken))
            {
                return null;
            }

            if (manifest != null)
            {
                await UpdateManifestAsync(feed, name, path.Version, manifest, cancellationToken);
            }

            LogMirrored(path.FilePath, downloaded.Mirror.PackageSource);

            return file;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, path.FilePath);

            return null;
        }
    }

    /// <summary>
    /// Creates the version a file belongs to, unless the feed has it already.
    /// </summary>
    private async Task EnsureVersionAsync(
        Feed feed,
        MavenPath path,
        JsonObject manifest,
        string cachedFrom,
        DateTime published,
        CancellationToken cancellationToken)
    {
        var name = path.PackageName;
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Version, cancellationToken) != null)
        {
            return;
        }

        // The POM may come after the other files; until then the manifest is the coordinates.
        manifest ??= MavenPomReader.CreateManifest(path.GroupId, path.ArtifactId, path.Version);

        // AlreadyExists means a concurrent upload of another file of the version created it.
        await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = path.Version,
                IsPrerelease = MavenVersion.Parse(path.Version).IsPrerelease,
                Listed = true,
                Description = MavenPomReader.GetDescription(manifest, MaxDescriptionLength),
                MetadataJson = manifest.ToJsonString(),
                CachedFrom = cachedFrom,
                Published = published,
            },
            cancellationToken);
    }

    private async Task UpdateManifestAsync(Feed feed, string name, string version, JsonObject manifest, CancellationToken cancellationToken)
    {
        await _artifacts.UpdateManifestAsync(
            feed.Id,
            name,
            version,
            MavenPomReader.GetDescription(manifest, MaxDescriptionLength),
            manifest.ToJsonString(),
            cancellationToken);
    }

    /// <summary>
    /// Checks an uploaded checksum against the stored file. A checksum of a file the feed doesn't
    /// have is ignored, since there is nothing to check it against.
    /// </summary>
    private async Task<MavenPutResult> CheckUploadedChecksumAsync(
        Feed feed,
        MavenPath path,
        MavenChecksum checksum,
        Stream content,
        CancellationToken cancellationToken)
    {
        string text;
        try
        {
            using var reader = new StreamReader(new LimitedReadStream(content, MaxChecksumBytes, () => new InvalidDataException()));
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Invalid("The checksum file is too large.");
        }

        // The file is the hex checksum, sometimes followed by the file name.
        var uploaded = text.Trim().Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : string.Empty;

        var file = await FindStoredFileAsync(feed, path.PackageName, path.Version, path.FileName, cancellationToken);
        if (file == null)
        {
            return MavenPutResult.Success;
        }

        var expected = MavenChecksums.GetValue(file, checksum);
        if (!string.Equals(uploaded, expected, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid($"The {checksum.ToString().ToUpperInvariant()} checksum {uploaded} does not match {path.FileName}, which has {expected}.");
        }

        return MavenPutResult.Success;
    }

    private async Task<ArtifactFile> FindStoredFileAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        var files = await _artifacts.FindFilesAsync(feed.Id, name, version, cancellationToken);

        return files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A POM uploaded to a path must be the POM of that path's artifact and version. Values that
    /// are properties (CI-friendly versions such as <c>${revision}</c>) can't be checked.
    /// </summary>
    private static void CheckCoordinates(JsonObject manifest, MavenPath path)
    {
        var groupId = MavenPomReader.GetString(manifest, "groupId");
        var artifactId = MavenPomReader.GetString(manifest, "artifactId");
        var version = MavenPomReader.GetString(manifest, "version");

        static bool Matches(string pomValue, string pathValue) =>
            pomValue == null || pomValue.Contains("${", StringComparison.Ordinal) || string.Equals(pomValue, pathValue, StringComparison.OrdinalIgnoreCase);

        if (!Matches(groupId, path.GroupId) || !Matches(artifactId, path.ArtifactId) || !Matches(version, path.Version))
        {
            throw new InvalidMavenPomException(
                $"The POM is for {groupId}:{artifactId}:{version}, but it was uploaded to {path.GroupId}:{path.ArtifactId}:{path.Version}.");
        }
    }

    private bool CanOverwrite(Feed feed, string version)
    {
        // Each build of a snapshot replaces the last; that is what snapshots are for.
        if (MavenVersion.IsSnapshot(version))
        {
            return true;
        }

        return _feedSettings.GetAllowPackageOverwrites(feed) switch
        {
            PackageOverwriteAllowed.True => true,
            PackageOverwriteAllowed.PrereleaseOnly => MavenVersion.Parse(version).IsPrerelease,
            _ => false,
        };
    }

    private long GetMaxBytes(Feed feed)
    {
        return _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
    }

    /// <summary>
    /// Whether downloading a file counts as a download of its version: the main file of the
    /// version's packaging, e.g. the JAR, or the POM of a version that is only a POM (a BOM or
    /// parent). Signatures, classified files and Gradle's module metadata don't count.
    /// </summary>
    private static bool IsMainFile(ArtifactVersion version, MavenFileName file)
    {
        if (file.Classifier != null || file.IsSignature || string.Equals(file.Extension, "module", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!file.IsPom)
        {
            return true;
        }

        return string.Equals(MavenPomSummary.Parse(version.MetadataJson).Packaging, "pom", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetContentType(MavenFileName file)
    {
        var extension = file.Extension.ToLowerInvariant();

        if (file.IsSignature)
        {
            return "application/pgp-signature";
        }

        return extension switch
        {
            "pom" or "xml" => "application/xml",
            "jar" or "war" or "ear" or "aar" => "application/java-archive",
            "module" or "json" => "application/json",
            _ => "application/octet-stream",
        };
    }

    private static string ComputeChecksum(byte[] content, MavenChecksum checksum)
    {
#pragma warning disable CA5350, CA5351 // SHA-1 and MD5 are what Maven clients check files with, not for security.
        var hash = checksum switch
        {
            MavenChecksum.Sha1 => SHA1.HashData(content),
            MavenChecksum.Md5 => MD5.HashData(content),
            MavenChecksum.Sha256 => SHA256.HashData(content),
            _ => SHA512.HashData(content),
        };
#pragma warning restore CA5350, CA5351

        return Convert.ToHexStringLower(hash);
    }

    private static MavenContent Text(string text)
    {
        return new MavenContent { Stream = new MemoryStream(Encoding.UTF8.GetBytes(text)), ContentType = "text/plain" };
    }

    private static MavenPutResult Invalid(string message)
    {
        return new MavenPutResult { Status = MavenPutStatus.Invalid, Message = message };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Maven file {FileName} of {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string fileName, string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A POM uploaded to {Path} in feed {FeedSlug} is invalid")]
    private partial void LogPomRejected(Exception exception, string path, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored Maven file {Path} from {PackageSource}")]
    private partial void LogMirrored(string path, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored POM {Path} could not be read, storing it without a manifest")]
    private partial void LogMirroredPomUnreadable(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror Maven file {Path}")]
    private partial void LogMirrorFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored Maven file {FileName} of {PackageName} {PackageVersion} in feed {FeedSlug} is missing from storage")]
    private partial void LogStoredFileMissing(string fileName, string packageName, string packageVersion, string feedSlug);
}
