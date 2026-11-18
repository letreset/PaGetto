using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Maven;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Ivy;

public partial class IvyRepositoryService : IIvyRepositoryService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    // What an uploaded checksum file may have.
    private const long MaxChecksumBytes = 1024;


    // The types whose files are not the module's artifact: the descriptor, POM, sources and docs.
    private static readonly string[] NonArtifactTypeFolders = ["ivys", "poms", "srcs", "docs"];

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly IvyUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<IvyRepositoryService> _logger;

    public IvyRepositoryService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        IvyUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<IvyRepositoryService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MavenContent> GetAsync(Feed feed, IvyPath path, bool countDownload, CancellationToken cancellationToken)
    {
        var name = path.PackageName;
        var version = await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Revision, cancellationToken);
        var file = version == null ? null : await FindStoredFileAsync(feed, name, path.Revision, path.FileName, cancellationToken);

        // A revision published to the feed hides the same revision of a mirror. Snapshots are
        // republished in place, so they are not mirrored.
        if (file == null && (version == null || version.CachedFrom != null) && !MavenVersion.IsSnapshot(path.Revision))
        {
            file = await MirrorAsync(feed, path, cancellationToken);
            version ??= await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Revision, cancellationToken);
        }

        if (file == null || version == null)
        {
            return null;
        }

        if (path.Checksum is { } checksum)
        {
            var text = MavenChecksums.GetValue(file, checksum);
            return new MavenContent { Stream = new MemoryStream(Encoding.UTF8.GetBytes(text)), ContentType = "text/plain" };
        }

        var stream = await _storage.GetFileStreamAsync(feed.Slug, name, path.Revision, file.FileName, cancellationToken);
        if (stream == null)
        {
            LogStoredFileMissing(file.FileName, name, path.Revision, feed.Slug);
            return null;
        }

        if (countDownload && IsMainArtifact(path))
        {
            await _artifacts.AddDownloadAsync(feed.Id, name, path.Revision, cancellationToken);
        }

        return new MavenContent { Stream = stream, ContentType = GetContentType(path.FileName), File = file };
    }

    public async Task<IReadOnlyList<string>> ListRevisionsAsync(Feed feed, IvyPath path, CancellationToken cancellationToken)
    {
        var local = (await _artifacts.FindVersionsAsync(feed.Id, path.PackageName, cancellationToken))
            .Where(v => v.Listed)
            .Select(v => v.Version);

        var upstream = await _upstream.ListRevisionsAsync(feed, path.FilePath, cancellationToken);

        // The feed's own spelling of a revision comes first, and wins.
        return local
            .Concat(upstream)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(MavenVersion.StringComparer)
            .ToList();
    }

    public async Task<MavenPutResult> PutAsync(Feed feed, IvyPath path, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        if (path.Checksum is { } checksum)
        {
            return await CheckUploadedChecksumAsync(feed, path, checksum, content, cancellationToken);
        }

        ArtifactFileContent file;
        try
        {
            file = await ArtifactFileContent.ReadAsync(content, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException e)
        {
            return new MavenPutResult { Status = MavenPutStatus.TooLarge, Message = e.Message };
        }

        using (file)
        {
            JsonObject manifest = null;
            if (path.IsDescriptor)
            {
                try
                {
                    manifest = IvyDescriptorReader.Read(file.Stream);
                    IvyDescriptorReader.CheckCoordinates(manifest, path);
                }
                catch (InvalidIvyDescriptorException e)
                {
                    LogDescriptorRejected(e, path.FilePath, feed.Slug);
                    return Invalid(e.Message);
                }

                file.Stream.Position = 0;
            }

            var name = path.PackageName;
            var existing = await FindStoredFileAsync(feed, name, path.Revision, path.FileName, cancellationToken);
            if (existing != null)
            {
                // Clients retry uploads, and the same content again changes nothing.
                if (string.Equals(existing.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new MavenPutResult { Status = MavenPutStatus.Success, StoredFile = true };
                }

                if (!CanOverwrite(feed, path.Revision))
                {
                    return new MavenPutResult
                    {
                        Status = MavenPutStatus.Conflict,
                        Message = $"{path.FileName} already exists in {IvyPath.GetDisplayName(path.Organisation, path.Module, path.ScalaVersion, path.SbtVersion)} {path.Revision}, and the feed does not allow overwriting it.",
                    };
                }
            }

            var now = _time.UtcNow;
            await EnsureVersionAsync(feed, path, manifest, cachedFrom: null, published: now, cancellationToken);

            await _storage.SaveFileAsync(feed.Slug, name, path.Revision, path.FileName, file.Stream, cancellationToken);
            if (!await _artifacts.AddOrReplaceFileAsync(feed.Id, name, path.Revision, file.ToArtifactFile(path.FileName, now), cancellationToken))
            {
                return new MavenPutResult { Status = MavenPutStatus.Conflict, Message = "The revision was deleted while the file was uploaded." };
            }

            if (manifest != null)
            {
                await UpdateManifestAsync(feed, name, path.Revision, manifest, cancellationToken);
            }

            LogPublished(path.FileName, name, path.Revision, feed.Slug);

            return new MavenPutResult { Status = MavenPutStatus.Success, StoredFile = true };
        }
    }

    /// <summary>
    /// Stores a file of a revision from the feed's mirrors in the feed.
    /// </summary>
    /// <returns>The stored file, or null if no mirror has it or it could not be stored.</returns>
    private async Task<ArtifactFile> MirrorAsync(Feed feed, IvyPath path, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetMirrorOptions(feed).Count == 0)
        {
            return null;
        }

        var name = path.PackageName;
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, $"{path.Revision}/{path.FileName}", cancellationToken);

        // A concurrent request may have stored the file while we waited for the lock.
        var stored = await FindStoredFileAsync(feed, name, path.Revision, path.FileName, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        try
        {
            var maxBytes = _feedSettings.GetMaxPackageSizeBytes(feed);
            using var downloaded = await _upstream.DownloadFileOrNullAsync(feed, path.FilePath, maxBytes, cancellationToken);
            if (downloaded == null)
            {
                return null;
            }

            var content = downloaded.Content;

            // The descriptor is only read for the web UI, so a mirrored one that can't be read is still served.
            JsonObject manifest = null;
            if (path.IsDescriptor)
            {
                try
                {
                    manifest = IvyDescriptorReader.Read(content.Stream);
                }
                catch (InvalidIvyDescriptorException e)
                {
                    LogMirroredDescriptorUnreadable(e, path.FilePath);
                }

                content.Stream.Position = 0;
            }

            var now = _time.UtcNow;
            var source = downloaded.Mirror.PackageSource.AbsoluteUri;
            await EnsureVersionAsync(feed, path, manifest, source, downloaded.LastModified ?? now, cancellationToken);

            var file = content.ToArtifactFile(path.FileName, now);
            await _storage.SaveFileAsync(feed.Slug, name, path.Revision, path.FileName, content.Stream, cancellationToken);
            if (!await _artifacts.AddOrReplaceFileAsync(feed.Id, name, path.Revision, file, cancellationToken))
            {
                return null;
            }

            if (manifest != null)
            {
                await UpdateManifestAsync(feed, name, path.Revision, manifest, cancellationToken);
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
    /// Creates the revision a file belongs to, unless the feed has it already.
    /// </summary>
    private async Task EnsureVersionAsync(
        Feed feed,
        IvyPath path,
        JsonObject manifest,
        string cachedFrom,
        DateTime published,
        CancellationToken cancellationToken)
    {
        var name = path.PackageName;
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, path.Revision, cancellationToken) != null)
        {
            return;
        }

        // sbt uploads the descriptor last; until then the manifest is the coordinates.
        manifest ??= IvyDescriptorReader.CreateManifest(path);

        // AlreadyExists means a concurrent upload of another file of the revision created it.
        await _artifacts.AddVersionAsync(
            feed.Id,
            name,
            new ArtifactVersion
            {
                Version = path.Revision,
                IsPrerelease = MavenVersion.Parse(path.Revision).IsPrerelease,
                Listed = true,
                Description = IvyDescriptorReader.GetDescription(manifest, MaxDescriptionLength),
                MetadataJson = manifest.ToJsonString(),
                CachedFrom = cachedFrom,
                Published = published,
            },
            cancellationToken);
    }

    private async Task UpdateManifestAsync(Feed feed, string name, string revision, JsonObject manifest, CancellationToken cancellationToken)
    {
        await _artifacts.UpdateManifestAsync(
            feed.Id,
            name,
            revision,
            IvyDescriptorReader.GetDescription(manifest, MaxDescriptionLength),
            manifest.ToJsonString(),
            cancellationToken);
    }

    /// <summary>
    /// Checks an uploaded checksum against the stored file. A checksum of a file the feed doesn't
    /// have is ignored, since there is nothing to check it against.
    /// </summary>
    private async Task<MavenPutResult> CheckUploadedChecksumAsync(
        Feed feed,
        IvyPath path,
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

        var file = await FindStoredFileAsync(feed, path.PackageName, path.Revision, path.FileName, cancellationToken);
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

    private async Task<ArtifactFile> FindStoredFileAsync(Feed feed, string name, string revision, string fileName, CancellationToken cancellationToken)
    {
        var files = await _artifacts.FindFilesAsync(feed.Id, name, revision, cancellationToken);

        return files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanOverwrite(Feed feed, string revision)
    {
        // sbt publishes a snapshot again in place; that is what snapshots are for.
        if (MavenVersion.IsSnapshot(revision))
        {
            return true;
        }

        return _feedSettings.CanOverwrite(feed, MavenVersion.Parse(revision).IsPrerelease);
    }

    /// <summary>
    /// Whether downloading a file counts as a download of its revision: the module's own artifact,
    /// such as <c>jars/sbt-tool.jar</c>. The descriptor, sources, docs, classified files and
    /// signatures don't count.
    /// </summary>
    private static bool IsMainArtifact(IvyPath path)
    {
        var slash = path.FileName.IndexOf('/');
        var typeFolder = path.FileName[..slash];
        var fileName = path.FileName[(slash + 1)..];

        return !NonArtifactTypeFolders.Contains(typeFolder, StringComparer.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileNameWithoutExtension(fileName), path.Module, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xml" or ".pom" => "application/xml",
            ".jar" or ".war" => "application/java-archive",
            ".asc" => "application/pgp-signature",
            _ => "application/octet-stream",
        };
    }

    private static MavenPutResult Invalid(string message)
    {
        return new MavenPutResult { Status = MavenPutStatus.Invalid, Message = message };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Ivy file {FileName} of {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string fileName, string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An ivy.xml uploaded to {Path} in feed {FeedSlug} is invalid")]
    private partial void LogDescriptorRejected(Exception exception, string path, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored Ivy file {Path} from {PackageSource}")]
    private partial void LogMirrored(string path, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored ivy.xml {Path} could not be read, storing it without a manifest")]
    private partial void LogMirroredDescriptorUnreadable(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror Ivy file {Path}")]
    private partial void LogMirrorFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored Ivy file {FileName} of {PackageName} {PackageVersion} in feed {FeedSlug} is missing from storage")]
    private partial void LogStoredFileMissing(string fileName, string packageName, string packageVersion, string feedSlug);
}
