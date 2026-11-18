using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Adds and removes versions with their content, the same way for every ecosystem.
/// </summary>
public static class ArtifactPublisher
{
    /// <summary>
    /// Stores the archive and readme of a version, then adds the version. If adding the version
    /// fails, the stored content is removed again: the storage refuses to replace content, so a
    /// leftover archive would make every later publish of that version with other content fail.
    /// </summary>
    /// <param name="artifacts">The feed's artifact database.</param>
    /// <param name="storage">The artifact storage.</param>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The package's stored name.</param>
    /// <param name="version">The version to add; <see cref="ArtifactVersion.HasReadme"/> is set here.</param>
    /// <param name="archive">The archive as clients download it.</param>
    /// <param name="readme">The readme, or null if the version has none.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    public static async Task<ArtifactAddResult> AddAsync(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        Feed feed,
        string name,
        ArtifactVersion version,
        Stream archive,
        string readme,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(version);

        version.HasReadme = readme != null;

        if (archive != null)
        {
            archive.Position = 0;
            using var readmeStream = readme == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(readme));

            await storage.SaveAsync(feed.Slug, name, version.Version, archive, readmeStream, cancellationToken);
        }

        try
        {
            return await artifacts.AddVersionAsync(feed.Id, name, version, cancellationToken);
        }
        catch (Exception) when (archive != null)
        {
            await RemoveUnusedContentAsync(artifacts, storage, feed, name, version.Version);
            throw;
        }
    }

    /// <summary>
    /// Removes a version: its row, its archive and readme, and its files.
    /// </summary>
    /// <returns>False if the feed doesn't have the version.</returns>
    public static async Task<bool> RemoveAsync(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        Feed feed,
        string name,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(feed);

        // The files are listed before the version and its file rows are gone.
        var files = await artifacts.FindFilesAsync(feed.Id, name, version, cancellationToken);

        if (!await artifacts.HardDeleteVersionAsync(feed.Id, name, version, cancellationToken))
        {
            return false;
        }

        await storage.DeleteAsync(feed.Slug, name, version, cancellationToken);
        foreach (var file in files)
        {
            await storage.DeleteFileAsync(feed.Slug, name, version, file.FileName, cancellationToken);
        }

        return true;
    }

    private static async Task RemoveUnusedContentAsync(
        IArtifactDatabase artifacts,
        IArtifactStorageService storage,
        Feed feed,
        string name,
        string version)
    {
        // Not cancelled, so a client that leaves doesn't leave the content behind. Another publish
        // of the same content may have added the version meanwhile; then the content is its own.
        try
        {
            if (await artifacts.FindVersionOrNullAsync(feed.Id, name, version, CancellationToken.None) == null)
            {
                await storage.DeleteAsync(feed.Slug, name, version, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // The failure to add the version is the one to report. The content stays behind, as
            // it did before this cleanup existed.
        }
    }
}
