using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.RubyGems;

public partial class RubyGemsPackageService : IRubyGemsPackageService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;

    private const long BytesPerMiB = 1024L * 1024;

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly RubyGemsUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly ILogger<RubyGemsPackageService> _logger;

    public RubyGemsPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        RubyGemsUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        ILogger<RubyGemsPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<byte[]> GetVersionsFileAsync(Feed feed, CancellationToken cancellationToken)
    {
        var gems = await FindAllGemsAsync(feed, cancellationToken);

        using var file = new MemoryStream();
        var created = gems.SelectMany(g => g.Versions).Select(v => v.Version.Published).DefaultIfEmpty(feed.UpdatedAtUtc).Max();
        file.Write(Encoding.UTF8.GetBytes(RubyGemsCompactIndex.VersionsHeader(created)));

        // The feed's gems come first: Bundler updates /versions by appending what is new at its
        // end, which is where the mirror's own additions land.
        foreach (var gem in gems)
        {
            var entries = new List<string>();
            foreach (var (version, manifest) in gem.Versions.Where(v => v.Version.Listed))
            {
                entries.AddRange(manifest.Gems.Select(g => RubyGemsCompactIndex.VersionEntry(version.Version, g.Platform)));
            }

            // A version unlisted in the feed is removed, which hides it if the mirror lists it.
            foreach (var (version, manifest) in gem.Versions.Where(v => !v.Version.Listed))
            {
                entries.AddRange(manifest.Gems.Select(g => "-" + RubyGemsCompactIndex.VersionEntry(version.Version, g.Platform)));
            }

            if (entries.Count == 0)
            {
                continue;
            }

            var checksum = RubyGemsCompactIndex.Md5(Encoding.UTF8.GetBytes(RubyGemsCompactIndex.InfoFile(MergeInfoLines(gem.Versions, []))));
            file.Write(Encoding.UTF8.GetBytes(RubyGemsCompactIndex.VersionsLine(gem.Name, entries, checksum)));
        }

        foreach (var upstream in await _upstream.GetVersionsFilesAsync(feed, cancellationToken))
        {
            file.Write(upstream);
            if (upstream.Length > 0 && upstream[^1] != (byte)'\n')
            {
                file.WriteByte((byte)'\n');
            }
        }

        return file.ToArray();
    }

    public async Task<byte[]> GetInfoFileOrNullAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        if (!RubyGemsPackageName.IsValid(name))
        {
            return null;
        }

        var versions = await FindVersionsAsync(feed, RubyGemsPackageName.Normalize(name), cancellationToken);

        // The mirror is asked for the gem by the name it was published under in the feed, if any.
        var publishedName = versions.Select(v => v.Manifest.Name).FirstOrDefault(n => n != null) ?? name;
        var upstream = await _upstream.ListVersionsAsync(feed, publishedName, cancellationToken);

        if (versions.Count == 0 && upstream.Count == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetBytes(RubyGemsCompactIndex.InfoFile(MergeInfoLines(versions, upstream)));
    }

    public async Task<byte[]> GetNamesFileAsync(Feed feed, CancellationToken cancellationToken)
    {
        var names = (await FindAllGemsAsync(feed, cancellationToken))
            .Select(g => g.Name)
            .Order(StringComparer.Ordinal);

        return Encoding.UTF8.GetBytes(RubyGemsCompactIndex.InfoFile(names));
    }

    public async Task<Stream> GetGemStreamOrNullAsync(Feed feed, string fullName, bool countDownload, CancellationToken cancellationToken)
    {
        foreach (var candidate in RubyGemsFileName.Parse(fullName))
        {
            var name = RubyGemsPackageName.Normalize(candidate.Name);
            var local = await _artifacts.FindVersionOrNullAsync(feed.Id, name, candidate.Version, cancellationToken);

            var file = local == null ? null : await FindStoredFileAsync(feed, name, local.Version, candidate.FullName + RubyGemsFileName.GemExtension, cancellationToken);
            if (file == null && (local == null || local.CachedFrom != null))
            {
                file = await MirrorAsync(feed, candidate, cancellationToken);
            }

            if (file == null)
            {
                continue;
            }

            var version = local?.Version ?? candidate.Version;
            var stream = await _storage.GetFileStreamAsync(feed.Slug, name, version, file.FileName, cancellationToken);
            if (stream == null)
            {
                LogStoredFileMissing(file.FileName, feed.Slug);
                return null;
            }

            if (countDownload)
            {
                await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);
            }

            return stream;
        }

        return null;
    }

    public async Task<byte[]> GetQuickSpecOrNullAsync(Feed feed, string fullName, CancellationToken cancellationToken)
    {
        foreach (var candidate in RubyGemsFileName.Parse(fullName))
        {
            var local = await _artifacts.FindVersionOrNullAsync(feed.Id, RubyGemsPackageName.Normalize(candidate.Name), candidate.Version, cancellationToken);

            var specification = local == null ? null : RubyGemsManifest.Parse(local.MetadataJson).Find(candidate.Platform);
            if (specification != null)
            {
                return RubyGemsQuickSpec.Create(specification);
            }

            // A version pushed to the feed hides the same version of the mirror.
            if (local != null && local.CachedFrom == null)
            {
                continue;
            }

            var upstream = await FindUpstreamAsync(feed, candidate, cancellationToken);
            var quickSpec = upstream == null ? null : await _upstream.GetQuickSpecOrNullAsync(feed, upstream, cancellationToken);
            if (quickSpec != null)
            {
                return quickSpec;
            }
        }

        return null;
    }

    public async Task<RubyGemsPushResult> PushAsync(Feed feed, Stream gem, long maxBytes, CancellationToken cancellationToken)
    {
        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(gem, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException e)
        {
            return new RubyGemsPushResult { Status = RubyGemsPushStatus.TooLarge, Message = e.Message };
        }

        using (content)
        {
            RubyGemsArchive archive;
            try
            {
                archive = await RubyGemsArchiveReader.ReadAsync(content.Stream, cancellationToken);
            }
            catch (InvalidRubyGemException e)
            {
                LogPushRejected(e, feed.Slug);
                return Invalid(null, null, e.Message);
            }

            var specification = archive.Specification;
            if (!RubyGemsPackageName.IsValid(specification.Name))
            {
                return Invalid(null, null,
                    $"'{specification.Name}' is not a valid gem name: it must be letters, digits, '.', '-' and '_', " +
                    $"with at least one letter, and have at most {RubyGemsPackageName.MaxLength} characters.");
            }

            if (!RubyGemsVersion.TryParse(specification.Version, out var version))
            {
                return Invalid(specification.Name, null, $"'{specification.Version}' is not a gem version such as 1.2.3 or 2.0.0.rc1.");
            }

            if (RubyGemsFileName.IsRubyPlatform(specification.Platform))
            {
                specification.Platform = RubyGemsFileName.RubyPlatform;
            }
            else if (!RubyGemsFileName.IsValidPlatform(specification.Platform))
            {
                return Invalid(specification.Name, version.Value, $"'{specification.Platform}' is not a platform such as x86_64-linux or java.");
            }

            specification.Sha256 = content.Sha256;

            var name = RubyGemsPackageName.Normalize(specification.Name);
            var fileName = specification.FullName + RubyGemsFileName.GemExtension;

            using var pushLock = await _mirrorLock.AcquireAsync(feed.Id, name, version.Value, cancellationToken);

            var versions = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);

            // Like rubygems.org, a gem keeps the name it was first pushed under, so the index agrees.
            var publishedName = versions.Select(v => RubyGemsManifest.Parse(v.MetadataJson).Name).FirstOrDefault(n => n != null);
            if (publishedName != null && publishedName != specification.Name)
            {
                return Invalid(specification.Name, version.Value, $"The gem is named {publishedName} in this feed, not {specification.Name}.");
            }

            var existing = versions.FirstOrDefault(v => string.Equals(v.Version, version.Value, StringComparison.OrdinalIgnoreCase));
            var existingFile = existing == null ? null : await FindStoredFileAsync(feed, name, existing.Version, fileName, cancellationToken);
            if (existingFile != null)
            {
                // Pushing the same gem again changes nothing.
                if (string.Equals(existingFile.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Success(specification);
                }

                if (!CanOverwrite(feed, version))
                {
                    return new RubyGemsPushResult
                    {
                        Status = RubyGemsPushStatus.Conflict,
                        Name = specification.Name,
                        Version = version.Value,
                        FullName = specification.FullName,
                        Message = $"Repushing of gem versions is not allowed: {specification.FullName} is in the feed already, and the feed does not allow overwriting it.",
                    };
                }
            }

            if (await StoreAsync(feed, name, specification, content, archive.Readme, existing, cachedFrom: null, cancellationToken) == null)
            {
                return new RubyGemsPushResult
                {
                    Status = RubyGemsPushStatus.Conflict,
                    Name = specification.Name,
                    Version = version.Value,
                    FullName = specification.FullName,
                    Message = "The version was deleted while the gem was pushed.",
                };
            }

            LogPublished(specification.FullName, feed.Slug);

            return Success(specification);
        }
    }

    public async Task<bool> YankAsync(Feed feed, string name, string version, string platform, CancellationToken cancellationToken)
    {
        if (!RubyGemsPackageName.IsValid(name) || string.IsNullOrEmpty(version))
        {
            return false;
        }

        name = RubyGemsPackageName.Normalize(name);
        var local = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
        if (local == null || (!string.IsNullOrEmpty(platform) && RubyGemsManifest.Parse(local.MetadataJson).Find(platform) == null))
        {
            return false;
        }

        return await _artifacts.SetListedAsync(feed.Id, name, local.Version, listed: false, cancellationToken);
    }

    /// <summary>
    /// Stores a gem file of a version from the feed's mirror in the feed.
    /// </summary>
    /// <returns>The stored file, or null if the mirror doesn't have it or it could not be stored.</returns>
    private async Task<ArtifactFile> MirrorAsync(Feed feed, RubyGemsFileName candidate, CancellationToken cancellationToken)
    {
        if (_feedSettings.GetMirrorOptions(feed).Count == 0)
        {
            return null;
        }

        var upstream = await FindUpstreamAsync(feed, candidate, cancellationToken);
        if (upstream == null)
        {
            return null;
        }

        var name = RubyGemsPackageName.Normalize(upstream.Name);
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, upstream.Version, cancellationToken);

        // A concurrent request may have stored the file while we waited for the lock.
        var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, name, upstream.Version, cancellationToken);
        var stored = existing == null ? null : await FindStoredFileAsync(feed, name, existing.Version, upstream.FullName + RubyGemsFileName.GemExtension, cancellationToken);
        if (stored != null)
        {
            return stored;
        }

        // A version pushed to the feed hides the same version of the mirror.
        if (existing != null && existing.CachedFrom == null)
        {
            return null;
        }

        try
        {
            using var content = await _upstream.DownloadGemOrNullAsync(feed, upstream, GetMaxBytes(feed), cancellationToken);
            if (content == null)
            {
                return null;
            }

            if (upstream.Sha256 != null && !string.Equals(upstream.Sha256, content.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The gem {upstream.FullName} has the SHA-256 {content.Sha256}, but the upstream lists {upstream.Sha256}.");
            }

            var archive = await RubyGemsArchiveReader.ReadAsync(content.Stream, cancellationToken);

            // The file is stored under the name, version and platform the mirror lists it with.
            var specification = archive.Specification;
            specification.Version = upstream.Version;
            specification.Platform = upstream.Platform;
            specification.Sha256 = content.Sha256;
            if (!string.Equals(specification.Name, upstream.Name, StringComparison.OrdinalIgnoreCase))
            {
                specification.Name = upstream.Name;
            }

            var file = await StoreAsync(feed, name, specification, content, archive.Readme, existing, upstream.Mirror.PackageSource.AbsoluteUri, cancellationToken);
            if (file != null)
            {
                LogMirrored(upstream.FullName, upstream.Mirror.PackageSource);
            }

            return file;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, upstream.FullName, upstream.Mirror.PackageSource);

            return null;
        }
    }

    /// <summary>
    /// Stores a gem file: the file, the version with its manifest if the feed doesn't have it yet,
    /// or else the file's gemspec in the version's manifest.
    /// </summary>
    /// <returns>The stored file, or null if the version was deleted meanwhile.</returns>
    private async Task<ArtifactFile> StoreAsync(
        Feed feed,
        string name,
        RubyGemsSpecification specification,
        ArtifactFileContent content,
        string readme,
        ArtifactVersion existing,
        string cachedFrom,
        CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var fileName = specification.FullName + RubyGemsFileName.GemExtension;

        // A version matched regardless of case keeps the case it has.
        var version = specification.Version = existing?.Version ?? specification.Version;

        content.Stream.Position = 0;
        await _storage.SaveFileAsync(feed.Slug, name, version, fileName, content.Stream, cancellationToken);

        if (existing == null)
        {
            var manifest = new RubyGemsManifest();
            manifest.AddOrReplace(specification);

            if (readme != null)
            {
                using var readmeStream = new MemoryStream(Encoding.UTF8.GetBytes(readme));
                await _storage.SaveReadmeAsync(feed.Slug, name, version, readmeStream, cancellationToken);
            }

            var result = await _artifacts.AddVersionAsync(
                feed.Id,
                name,
                new ArtifactVersion
                {
                    Version = version,
                    IsPrerelease = RubyGemsVersion.Parse(version).IsPrerelease,
                    Listed = true,
                    Description = Truncate(specification.Summary),
                    MetadataJson = manifest.ToJson(),
                    HasReadme = readme != null,
                    CachedFrom = cachedFrom,
                    Published = now,
                },
                cancellationToken);

            // Another instance created the version meanwhile; the gemspec is added to it below.
            existing = result == ArtifactAddResult.AlreadyExists
                ? await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken)
                : null;
        }

        if (existing != null)
        {
            var manifest = RubyGemsManifest.Parse(existing.MetadataJson);
            manifest.AddOrReplace(specification);

            if (!await _artifacts.UpdateManifestAsync(feed.Id, name, version, Truncate(manifest.Main.Summary), manifest.ToJson(), cancellationToken))
            {
                return null;
            }
        }

        var file = content.ToArtifactFile(fileName, now);
        return await _artifacts.AddOrReplaceFileAsync(feed.Id, name, version, file, cancellationToken) ? file : null;
    }

    /// <summary>
    /// Finds the gem file of a full name on the feed's mirror.
    /// </summary>
    private async Task<RubyGemsUpstreamVersion> FindUpstreamAsync(Feed feed, RubyGemsFileName candidate, CancellationToken cancellationToken)
    {
        return (await _upstream.ListVersionsAsync(feed, candidate.Name, cancellationToken))
            .FirstOrDefault(u => u.Version == candidate.Version && u.Platform == candidate.Platform);
    }

    /// <summary>
    /// The info lines of a gem: those of the gem files of its listed versions in the feed, and
    /// those of the mirror that the feed doesn't have. A version pushed to the feed, or unlisted in
    /// it, hides the same version of the mirror.
    /// </summary>
    private static IEnumerable<string> MergeInfoLines(IReadOnlyList<StoredVersion> versions, IReadOnlyList<RubyGemsUpstreamVersion> upstream)
    {
        var lines = new List<(string Version, string Platform, string Line)>();
        foreach (var (version, manifest) in versions.Where(v => v.Version.Listed))
        {
            foreach (var gem in manifest.Gems.Where(g => g.Sha256 != null))
            {
                lines.Add((version.Version, gem.Platform, RubyGemsCompactIndex.InfoLine(gem)));
            }
        }

        var local = new Dictionary<string, StoredVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions)
        {
            local.TryAdd(version.Version.Version, version);
        }

        foreach (var file in upstream)
        {
            if (local.TryGetValue(file.Version, out var stored)
                && (stored.Version.CachedFrom == null || !stored.Version.Listed || stored.Manifest.Find(file.Platform) != null))
            {
                continue;
            }

            lines.Add((file.Version, file.Platform, file.Line));
        }

        return lines
            .OrderBy(l => l.Version, RubyGemsVersion.StringComparer)
            .ThenBy(l => RubyGemsFileName.IsRubyPlatform(l.Platform) ? 0 : 1)
            .ThenBy(l => l.Platform, StringComparer.Ordinal)
            .Select(l => l.Line);
    }

    private async Task<IReadOnlyList<StoredVersion>> FindVersionsAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        return (await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken))
            .Select(v => new StoredVersion(v, RubyGemsManifest.Parse(v.MetadataJson)))
            .ToList();
    }

    /// <summary>
    /// Every gem of the feed with its versions, ordered by name.
    /// </summary>
    private async Task<IReadOnlyList<StoredGem>> FindAllGemsAsync(Feed feed, CancellationToken cancellationToken)
    {
        var artifacts = (await _artifacts.SearchAsync(feed.Id, query: null, skip: 0, take: int.MaxValue, cancellationToken)).Artifacts;

        return artifacts
            .Select(a =>
            {
                var versions = a.Versions
                    .OrderBy(v => v.Version, RubyGemsVersion.StringComparer)
                    .Select(v => new StoredVersion(v, RubyGemsManifest.Parse(v.MetadataJson)))
                    .ToList();

                return new StoredGem(versions.Select(v => v.Manifest.Name).FirstOrDefault(n => n != null) ?? a.Name, versions);
            })
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ArtifactFile> FindStoredFileAsync(Feed feed, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        var files = await _artifacts.FindFilesAsync(feed.Id, name, version, cancellationToken);

        return files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanOverwrite(Feed feed, RubyGemsVersion version)
    {
        return _feedSettings.GetAllowPackageOverwrites(feed) switch
        {
            PackageOverwriteAllowed.True => true,
            PackageOverwriteAllowed.PrereleaseOnly => version.IsPrerelease,
            _ => false,
        };
    }

    private long GetMaxBytes(Feed feed)
    {
        return _feedSettings.GetMaxPackageSizeMiB(feed) * BytesPerMiB;
    }

    private static string Truncate(string text)
    {
        text = text?.Trim();
        return text?.Length > MaxDescriptionLength ? text[..MaxDescriptionLength] : text;
    }

    private static RubyGemsPushResult Success(RubyGemsSpecification specification)
    {
        return new RubyGemsPushResult
        {
            Status = RubyGemsPushStatus.Success,
            Name = specification.Name,
            Version = specification.Version,
            FullName = specification.FullName,
        };
    }

    private static RubyGemsPushResult Invalid(string name, string version, string message)
    {
        return new RubyGemsPushResult { Status = RubyGemsPushStatus.Invalid, Name = name, Version = version, Message = message };
    }

    private sealed record StoredVersion(ArtifactVersion Version, RubyGemsManifest Manifest);

    private sealed record StoredGem(string Name, IReadOnlyList<StoredVersion> Versions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published gem {FullName} to feed {FeedSlug}")]
    private partial void LogPublished(string fullName, string feedSlug);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A gem pushed to feed {FeedSlug} is invalid")]
    private partial void LogPushRejected(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored gem {FullName} from {PackageSource}")]
    private partial void LogMirrored(string fullName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror gem {FullName} from {PackageSource}")]
    private partial void LogMirrorFailed(Exception exception, string fullName, Uri packageSource);

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored gem file {FileName} in feed {FeedSlug} is missing from storage")]
    private partial void LogStoredFileMissing(string fileName, string feedSlug);
}
