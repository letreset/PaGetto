using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Indexing;
using PaGetto.Core.Storage;

namespace PaGetto.Core.Quarantine;

/// <summary>
/// Copies or moves a stored version from one feed to another of the same type. The content is
/// copied as stored, so its hashes stay the same, and the target feed's quarantine policies apply
/// to the copy as to any new version. The caller checks the permissions. Scoped.
/// </summary>
public class PromotionService
{
    /// <summary>
    /// The audit and webhook event of a version promoted to a feed, sent for the target feed.
    /// </summary>
    public const string PromotedEvent = "package_promoted";

    private readonly IArtifactDatabase _artifacts;
    private readonly IArtifactStorageService _artifactStorage;
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _packageStorage;
    private readonly IPackageIndexingService _indexer;
    private readonly QuarantinePolicy _policy;

    public PromotionService(
        IArtifactDatabase artifacts,
        IArtifactStorageService artifactStorage,
        IPackageDatabase packages,
        IPackageStorageService packageStorage,
        IPackageIndexingService indexer,
        QuarantinePolicy policy)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _artifactStorage = artifactStorage ?? throw new ArgumentNullException(nameof(artifactStorage));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _packageStorage = packageStorage ?? throw new ArgumentNullException(nameof(packageStorage));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Copies a version to another feed, and removes it from its feed when it is moved.
    /// </summary>
    /// <param name="source">The feed that has the version.</param>
    /// <param name="target">The feed to copy it to, of the same type.</param>
    /// <param name="name">The package's stored name (the id of a NuGet package).</param>
    /// <param name="version">The version.</param>
    /// <param name="move">Whether the version is removed from <paramref name="source"/> afterwards.</param>
    /// <param name="actor">Who promotes it.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public async Task<PromotionResult> PromoteAsync(Feed source, Feed target, string name, string version, bool move, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source.Id == target.Id || source.Type != target.Type)
        {
            return PromotionResult.InvalidTarget;
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
        {
            return PromotionResult.NotFound;
        }

        var result = source.Type == FeedType.NuGet
            ? await PromoteNuGetAsync(source, target, name, version, move, cancellationToken)
            : await PromoteArtifactAsync(source, target, name, version, move, cancellationToken);

        if (result == PromotionResult.Success)
        {
            var detail = $"{(move ? "Moved" : "Copied")} from feed {source.Slug}.";
            await _policy.RecordAsync(PromotedEvent, target.Slug, name, version, actor, detail, null, cancellationToken);
        }

        return result;
    }

    private async Task<PromotionResult> PromoteNuGetAsync(Feed source, Feed target, string id, string versionText, bool move, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(versionText, out var version))
        {
            return PromotionResult.NotFound;
        }

        var package = await _packages.FindOrNullAsync(source.Id, id, version, includeUnlisted: true, cancellationToken);
        if (package == null)
        {
            return PromotionResult.NotFound;
        }

        if (await _packages.ExistsAsync(target.Id, package.Id, version, cancellationToken))
        {
            return PromotionResult.AlreadyExists;
        }

        using (var content = await CopyToTempFileAsync(await _packageStorage.GetPackageStreamAsync(source.Slug, package.Id, version, cancellationToken), cancellationToken))
        {
            if (content == null)
            {
                return PromotionResult.NotFound;
            }

            var indexed = await _indexer.IndexAsync(target.Id, target.Slug, content, cacheFeedUrl: null, package.Published, cancellationToken);
            if (indexed == PackageIndexingResult.PackageAlreadyExists)
            {
                return PromotionResult.AlreadyExists;
            }

            if (indexed != PackageIndexingResult.Success)
            {
                return PromotionResult.Invalid;
            }
        }

        if (!package.Listed)
        {
            await _packages.UnlistPackageAsync(target.Id, package.Id, version, cancellationToken);
        }

        if (move && await _packages.HardDeletePackageAsync(source.Id, package.Id, version, cancellationToken))
        {
            await _packageStorage.DeleteAsync(source.Slug, package.Id, version, cancellationToken);
        }

        return PromotionResult.Success;
    }

    private async Task<PromotionResult> PromoteArtifactAsync(Feed source, Feed target, string name, string versionText, bool move, CancellationToken cancellationToken)
    {
        var stored = await _artifacts.FindVersionOrNullAsync(source.Id, name, versionText, cancellationToken);
        if (stored == null)
        {
            return PromotionResult.NotFound;
        }

        if (await _artifacts.FindVersionOrNullAsync(target.Id, name, stored.Version, cancellationToken) != null)
        {
            return PromotionResult.AlreadyExists;
        }

        string readme = null;
        if (stored.HasReadme)
        {
            await using var readmeStream = await _artifactStorage.GetReadmeStreamAsync(source.Slug, name, stored.Version, cancellationToken);
            if (readmeStream != null)
            {
                using var reader = new StreamReader(readmeStream);
                readme = await reader.ReadToEndAsync(cancellationToken);
            }
        }

        var copy = new ArtifactVersion
        {
            Version = stored.Version,
            IsPrerelease = stored.IsPrerelease,
            Listed = stored.Listed,
            Description = stored.Description,
            MetadataJson = stored.MetadataJson,
            ContentHash = stored.ContentHash,
            Size = stored.Size,
            Published = stored.Published,
        };

        var files = await _artifacts.FindFilesAsync(source.Id, name, stored.Version, cancellationToken);

        // Only a version with a content hash has an archive; the versions of Maven, Ivy, PyPI,
        // RubyGems and VS Code have files instead, and a CocoaPods pod may have neither.
        using (var archive = stored.ContentHash == null
            ? null
            : await CopyToTempFileAsync(await _artifactStorage.GetArchiveStreamAsync(source.Slug, name, stored.Version, cancellationToken), cancellationToken))
        {
            if (archive == null && files.Count == 0)
            {
                return PromotionResult.NotFound;
            }

            var added = await ArtifactPublisher.AddAsync(_artifacts, _artifactStorage, target, name, copy, archive, readme, cancellationToken);
            if (added == ArtifactAddResult.AlreadyExists)
            {
                return PromotionResult.AlreadyExists;
            }

            // The readme is stored with the archive; without one it is stored on its own.
            if (archive == null && readme != null)
            {
                using var readmeStream = new MemoryStream(Encoding.UTF8.GetBytes(readme));
                await _artifactStorage.SaveReadmeAsync(target.Slug, name, stored.Version, readmeStream, cancellationToken);
            }
        }

        foreach (var file in files)
        {
            await using (var content = await _artifactStorage.GetFileStreamAsync(source.Slug, name, stored.Version, file.FileName, cancellationToken))
            {
                if (content == null)
                {
                    continue;
                }

                await _artifactStorage.SaveFileAsync(target.Slug, name, stored.Version, file.FileName, content, cancellationToken);
            }

            await _artifacts.AddOrReplaceFileAsync(target.Id, name, stored.Version, new ArtifactFile
            {
                FileName = file.FileName,
                Size = file.Size,
                Sha1 = file.Sha1,
                Md5 = file.Md5,
                Sha256 = file.Sha256,
                Sha512 = file.Sha512,
                CreatedAtUtc = file.CreatedAtUtc,
            }, cancellationToken);
        }

        if (move)
        {
            await ArtifactPublisher.RemoveAsync(_artifacts, _artifactStorage, source, name, stored.Version, cancellationToken);
        }

        return PromotionResult.Success;
    }

    /// <summary>
    /// The content as a seekable temporary file, deleted when it is disposed, or null for none.
    /// </summary>
    private static async Task<Stream> CopyToTempFileAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content == null)
        {
            return null;
        }

        await using (content)
        {
            var file = new FileStream(
                Path.GetTempFileName(),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await content.CopyToAsync(file, cancellationToken);
                file.Position = 0;

                return file;
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }
        }
    }
}
