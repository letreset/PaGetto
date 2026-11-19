using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Core.Upstream;

namespace PaGetto.Core.Hex;

public partial class HexPackageService : IHexPackageService
{
    // The length of the description column.
    private const int MaxDescriptionLength = 4000;


    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _storage;
    private readonly HexUpstreamClient _upstream;
    private readonly PackageMirrorLock _mirrorLock;
    private readonly IFeedSettingsResolver _feedSettings;
    private readonly SystemTime _time;
    private readonly MaliciousPackageGuard _malicious;
    private readonly ILogger<HexPackageService> _logger;

    public HexPackageService(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        HexUpstreamClient upstream,
        PackageMirrorLock mirrorLock,
        IFeedSettingsResolver feedSettings,
        SystemTime time,
        MaliciousPackageGuard malicious,
        ILogger<HexPackageService> logger)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _mirrorLock = mirrorLock ?? throw new ArgumentNullException(nameof(mirrorLock));
        _feedSettings = feedSettings ?? throw new ArgumentNullException(nameof(feedSettings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _malicious = malicious ?? throw new ArgumentNullException(nameof(malicious));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<HexRelease>> FindReleasesAsync(Feed feed, string name, CancellationToken cancellationToken)
    {
        if (!HexPackageName.IsValid(name))
        {
            return [];
        }

        var releases = new Dictionary<string, HexRelease>(StringComparer.OrdinalIgnoreCase);

        foreach (var upstream in await _upstream.ListReleasesAsync(feed, name, cancellationToken))
        {
            releases[upstream.Release.Version] = upstream.Release;
        }

        foreach (var local in await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken))
        {
            if (ToRelease(feed, local) is { } release)
            {
                releases[local.Version] = release;
            }
        }

        return releases.Values.OrderBy(r => r.Version, HexVersion.StringComparer).ToList();
    }

    public async Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<HexRelease>>>> FindPackagesAsync(Feed feed, CancellationToken cancellationToken)
    {
        var result = await _artifacts.SearchAsync(feed.Id, query: null, skip: 0, take: int.MaxValue, cancellationToken);

        var packages = new List<KeyValuePair<string, IReadOnlyList<HexRelease>>>();
        foreach (var artifact in result.Artifacts.OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            var releases = artifact.Versions
                .OrderBy(v => v.Version, HexVersion.StringComparer)
                .Select(v => ToRelease(feed, v))
                .Where(r => r != null)
                .ToList();

            if (releases.Count > 0)
            {
                packages.Add(KeyValuePair.Create<string, IReadOnlyList<HexRelease>>(artifact.Name, releases));
            }
        }

        return packages;
    }

    public async Task<Stream> GetTarballStreamOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        if (!HexPackageName.IsValid(name) || !HexVersion.TryParse(version, out _))
        {
            return null;
        }

        var stored = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
        await _malicious.EnsureAllowedAsync(feed, name, version, stored, cancellationToken);
        if (stored == null && !await MirrorAsync(feed, name, version, cancellationToken))
        {
            return null;
        }

        await _artifacts.AddDownloadAsync(feed.Id, name, version, cancellationToken);

        return await _storage.GetArchiveStreamAsync(feed.Slug, name, version, cancellationToken);
    }

    public async Task<HexPublishResult> PublishAsync(Feed feed, Stream tarball, long maxBytes, bool replace, CancellationToken cancellationToken)
    {
        ArtifactFileContent content;
        try
        {
            content = await ArtifactFileContent.ReadAsync(tarball, maxBytes, cancellationToken);
        }
        catch (ArtifactFileTooLargeException e)
        {
            return new HexPublishResult { Status = HexPublishStatus.TooLarge, Message = e.Message };
        }

        using (content)
        {
            return await PublishAsync(feed, content.Stream, replace, cancellationToken);
        }
    }

    private async Task<HexPublishResult> PublishAsync(Feed feed, FileStream tarball, bool replace, CancellationToken cancellationToken)
    {
        HexManifest manifest;
        string readme;
        try
        {
            tarball.Position = 0;
            (manifest, readme) = await HexTarballReader.ReadAsync(tarball, cancellationToken);
        }
        catch (InvalidHexPackageException e)
        {
            LogInvalidPackage(e, feed.Slug);

            return Invalid(null, null, e.Message);
        }

        var name = manifest.Name;
        if (!HexPackageName.IsValid(name))
        {
            return Invalid(null, null,
                "The package name must be lowercase ASCII letters, digits and '_', start with a letter, " +
                $"and have at most {HexPackageName.MaxLength} characters.");
        }

        if (!HexVersion.TryParse(manifest.Version, out var version))
        {
            return Invalid(name, null, "The version of the package must be a semantic version, e.g. 1.2.3.");
        }

        var versionText = version.ToString();
        var existingVersions = await _artifacts.FindVersionsAsync(feed.Id, name, cancellationToken);

        // Versions that differ only in their build metadata are the same version.
        var existing = existingVersions.FirstOrDefault(v =>
            HexVersion.TryParse(v.Version, out var other)
            && string.Equals(other.Precedence, version.Precedence, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (!_feedSettings.CanOverwrite(feed, version.IsPrerelease))
            {
                return AlreadyExists(name, existing.Version, $"Version {existing.Version} of package {name} already exists.");
            }

            if (!replace)
            {
                return AlreadyExists(name, existing.Version,
                    $"Version {existing.Version} of package {name} already exists. Publish with --replace to replace it.");
            }

            await _artifacts.HardDeleteVersionAsync(feed.Id, name, existing.Version, cancellationToken);
            await _storage.DeleteAsync(feed.Slug, name, existing.Version, cancellationToken);
        }

        var result = await IndexAsync(feed, name, version, manifest, readme, tarball, cachedFrom: null, cancellationToken);
        if (result == ArtifactAddResult.AlreadyExists)
        {
            return AlreadyExists(name, versionText, $"Version {versionText} of package {name} already exists.");
        }

        LogPublished(name, versionText, feed.Slug);

        return new HexPublishResult { Status = HexPublishStatus.Success, Name = name, Version = versionText };
    }

    public async Task<bool> SetRetirementAsync(Feed feed, string name, string version, HexRetirement retirement, CancellationToken cancellationToken)
    {
        if (!HexPackageName.IsValid(name) || string.IsNullOrEmpty(version))
        {
            return false;
        }

        var existing = await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var manifest = HexManifest.Parse(existing.MetadataJson).WithRetirement(retirement);

        return await _artifacts.UpdateVersionAsync(feed.Id, name, existing.Version, listed: retirement == null, manifest.ToJson(), cancellationToken);
    }

    /// <summary>
    /// The registry's view of a version stored in the feed. An unlisted version is retired.
    /// </summary>
    /// <returns>The release, or null if the version has no valid checksums.</returns>
    private static HexRelease ToRelease(Feed feed, ArtifactVersion version)
    {
        var manifest = HexManifest.Parse(version.MetadataJson);
        if (!TryFromHex(manifest.InnerChecksum, out var innerChecksum) || !TryFromHex(version.ContentHash, out var outerChecksum))
        {
            return null;
        }

        return new HexRelease
        {
            Version = version.Version,
            InnerChecksum = innerChecksum,
            OuterChecksum = outerChecksum,

            // A dependency in this feed's own repository is listed without one, as Hex does.
            Requirements = manifest.Requirements
                .Select(r => r.Repository == feed.Slug
                    ? new HexRequirement { Name = r.Name, Requirement = r.Requirement, Optional = r.Optional, App = r.App }
                    : r)
                .ToList(),
            Retirement = version.Listed ? null : manifest.Retirement ?? new HexRetirement { Reason = "other" },
            PublishedAt = version.Published,
        };
    }

    /// <summary>
    /// Stores a version from the feed's mirrors in the feed, unless it is there already.
    /// </summary>
    /// <returns>True if the feed has the version afterwards.</returns>
    private async Task<bool> MirrorAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        using var mirrorLock = await _mirrorLock.AcquireAsync(feed.Id, name, version, cancellationToken);

        // A concurrent request may have mirrored the version while we waited for the lock.
        if (await _artifacts.FindVersionOrNullAsync(feed.Id, name, version, cancellationToken) != null)
        {
            return true;
        }

        var upstream = (await _upstream.ListReleasesAsync(feed, name, cancellationToken))
            .FirstOrDefault(r => string.Equals(r.Release.Version, version, StringComparison.OrdinalIgnoreCase));

        if (upstream == null || !HexVersion.TryParse(upstream.Release.Version, out var parsedVersion))
        {
            return false;
        }

        try
        {
            using var tarball = await _upstream.DownloadTarballOrNullAsync(feed, name, upstream, _feedSettings.GetMaxPackageSizeBytes(feed), cancellationToken);
            if (tarball == null)
            {
                return false;
            }

            var hash = await SHA256.HashDataAsync(tarball, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(hash, upstream.Release.OuterChecksum))
            {
                throw new InvalidDataException(
                    $"The tarball of {name} {version} has the SHA-256 {Convert.ToHexStringLower(hash)}, " +
                    $"but the upstream lists {Convert.ToHexStringLower(upstream.Release.OuterChecksum)}.");
            }

            // The description, links and readme are only shown in the web UI, so a mirrored package
            // whose metadata can't be read is still served, with the registry's data.
            HexManifest metadata = null;
            string readme = null;
            try
            {
                tarball.Position = 0;
                (metadata, readme) = await HexTarballReader.ReadAsync(tarball, cancellationToken);
            }
            catch (InvalidHexPackageException e)
            {
                LogMirroredPackageUnreadable(e, name, version);
            }

            // The dependencies and checksum are the registry's, which clients have seen already;
            // the tarball's dependencies name the repository it was published to.
            var manifest = new HexManifest
            {
                Name = name,
                Version = upstream.Release.Version,
                Description = metadata?.Description,
                App = metadata?.App,
                Licenses = metadata?.Licenses ?? [],
                Links = metadata?.Links ?? new Dictionary<string, string>(),
                BuildTools = metadata?.BuildTools ?? [],
                Elixir = metadata?.Elixir,
                Requirements = upstream.Release.Requirements,
                InnerChecksum = Convert.ToHexStringLower(upstream.Release.InnerChecksum),
                Retirement = upstream.Release.Retirement,
            };

            var result = await IndexAsync(
                feed,
                name,
                parsedVersion,
                manifest,
                readme,
                tarball,
                cachedFrom: upstream.Mirror.PackageSource.AbsoluteUri,
                cancellationToken,
                listed: upstream.Release.Retirement == null);

            LogMirrored(name, version, upstream.Mirror.PackageSource, result);

            // AlreadyExists means another instance stored the version meanwhile, so it is available.
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogMirrorFailed(e, name, version);

            return false;
        }
    }

    private async Task<ArtifactAddResult> IndexAsync(
        Feed feed,
        string name,
        HexVersion version,
        HexManifest manifest,
        string readme,
        Stream tarball,
        string cachedFrom,
        CancellationToken cancellationToken,
        bool listed = true)
    {
        tarball.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(tarball, cancellationToken));

        var description = manifest.Description?.Trim();

        return await ArtifactPublisher.AddAsync(
            _artifacts,
            _storage,
            feed,
            name,
            new ArtifactVersion
            {
                Version = version.ToString(),
                IsPrerelease = version.IsPrerelease,
                Listed = listed,
                Description = description?.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
                MetadataJson = manifest.ToJson(),
                ContentHash = hash,
                Size = tarball.Length,
                CachedFrom = cachedFrom,
                Published = _time.UtcNow,
            },
            tarball,
            readme,
            cancellationToken);
    }

    private static bool TryFromHex(string hex, out byte[] bytes)
    {
        bytes = null;
        if (hex is not { Length: 64 })
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HexPublishResult Invalid(string name, string version, string message)
    {
        return new HexPublishResult { Status = HexPublishStatus.InvalidPackage, Name = name, Version = version, Message = message };
    }

    private static HexPublishResult AlreadyExists(string name, string version, string message)
    {
        return new HexPublishResult { Status = HexPublishStatus.AlreadyExists, Name = name, Version = version, Message = message };
    }


    [LoggerMessage(Level = LogLevel.Warning, Message = "A Hex package published to feed {FeedSlug} is invalid")]
    private partial void LogInvalidPackage(Exception exception, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published Hex package {PackageName} {PackageVersion} to feed {FeedSlug}")]
    private partial void LogPublished(string packageName, string packageVersion, string feedSlug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mirrored Hex package {PackageName} {PackageVersion} from {PackageSource} with result {Result}")]
    private partial void LogMirrored(string packageName, string packageVersion, Uri packageSource, ArtifactAddResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mirrored Hex package {PackageName} {PackageVersion} could not be read, storing it with the registry's data")]
    private partial void LogMirroredPackageUnreadable(Exception exception, string packageName, string packageVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror Hex package {PackageName} {PackageVersion}")]
    private partial void LogMirrorFailed(Exception exception, string packageName, string packageVersion);
}
