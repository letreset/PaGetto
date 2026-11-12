using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// The state of the packages of feeds that are not NuGet feeds (see <see cref="Artifact"/>). It is
/// shared by every such ecosystem; their content is stored by the <see cref="IArtifactStorageService"/>.
/// Names must already be normalized by the ecosystem. Versions are matched regardless of case.
/// </summary>
public interface IArtifactDatabase
{
    /// <summary>
    /// Finds all versions of a package, including unlisted ones.
    /// </summary>
    /// <returns>The versions found, each with its <see cref="ArtifactVersion.Artifact"/>. Always non-null.</returns>
    Task<IReadOnlyList<ArtifactVersion>> FindVersionsAsync(Guid feedId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Finds one version of a package, listed or not.
    /// </summary>
    /// <returns>The version with its <see cref="ArtifactVersion.Artifact"/>, or null.</returns>
    Task<ArtifactVersion> FindVersionOrNullAsync(Guid feedId, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a version, creating the package when this is its first version.
    /// </summary>
    Task<ArtifactAddResult> AddVersionAsync(Guid feedId, string name, ArtifactVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Lists or unlists a version.
    /// </summary>
    /// <returns>False if the version does not exist.</returns>
    Task<bool> SetListedAsync(Guid feedId, string name, string version, bool listed, CancellationToken cancellationToken);

    /// <summary>
    /// Changes whether a version is listed together with its manifest, e.g. for an npm deprecation,
    /// whose message is part of the manifest.
    /// </summary>
    /// <returns>False if the version does not exist.</returns>
    Task<bool> UpdateVersionAsync(Guid feedId, string name, string version, bool listed, string metadataJson, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a version and the tags that point at it, and the package itself once it has no
    /// versions left.
    /// </summary>
    /// <returns>False if the version does not exist.</returns>
    Task<bool> HardDeleteVersionAsync(Guid feedId, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Increments a version's download count.
    /// </summary>
    Task AddDownloadAsync(Guid feedId, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the packages whose name or description contains <paramref name="query"/>, ordered with
    /// name matches first. A null or empty query matches every package of the feed.
    /// </summary>
    Task<ArtifactSearchResult> SearchAsync(Guid feedId, string query, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the tags of a package.
    /// </summary>
    /// <returns>The tags. Empty if the package does not exist.</returns>
    Task<IReadOnlyList<ArtifactTag>> FindTagsAsync(Guid feedId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Points a tag at a version of the package, creating the tag or moving it.
    /// </summary>
    /// <returns>False if the package has no such version.</returns>
    Task<bool> SetTagAsync(Guid feedId, string name, string tag, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a tag of a package.
    /// </summary>
    /// <returns>False if the package has no such tag.</returns>
    Task<bool> RemoveTagAsync(Guid feedId, string name, string tag, CancellationToken cancellationToken);
}

/// <summary>
/// The result of <see cref="IArtifactDatabase.AddVersionAsync"/>.
/// </summary>
public enum ArtifactAddResult
{
    /// <summary>
    /// The package already has this version.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The version was added.
    /// </summary>
    Success,
}
