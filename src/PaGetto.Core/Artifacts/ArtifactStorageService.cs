using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Storage;

namespace PaGetto.Core.Artifacts;

public class ArtifactStorageService : IArtifactStorageService
{
    // The same root as NuGet packages. Feed slugs are unique across feed types, so the feeds'
    // folders never overlap.
    private const string PackagesPathPrefix = "packages";

    private const string ArchiveFileName = "archive";
    private const string ReadmeFileName = "readme";

    // The files of versions that have several (Maven), next to the archive and readme of the others.
    private const string FilesFolderName = "files";

    private const string ArchiveContentType = "binary/octet-stream";
    private const string ReadmeContentType = "text/markdown";

    private readonly IStorageService _storage;

    public ArtifactStorageService(IStorageService storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task SaveAsync(string feedSlug, string name, string version, Stream archive, Stream readme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var result = await _storage.PutAsync(GetPath(feedSlug, name, version, ArchiveFileName), archive, ArchiveContentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            throw new InvalidOperationException($"Failed to store package {name} {version} due to conflict");
        }

        if (readme != null)
        {
            result = await _storage.PutAsync(GetPath(feedSlug, name, version, ReadmeFileName), readme, ReadmeContentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                throw new InvalidOperationException($"Failed to store package {name} {version} readme due to conflict");
            }
        }
    }

    public async Task SaveReadmeAsync(string feedSlug, string name, string version, Stream readme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readme);

        await ReplaceAsync(GetPath(feedSlug, name, version, ReadmeFileName), readme, ReadmeContentType, cancellationToken);
    }

    public async Task<Stream> GetArchiveStreamAsync(string feedSlug, string name, string version, CancellationToken cancellationToken)
    {
        return await _storage.GetAsync(GetPath(feedSlug, name, version, ArchiveFileName), cancellationToken);
    }

    public async Task<Stream> GetReadmeStreamAsync(string feedSlug, string name, string version, CancellationToken cancellationToken)
    {
        return await _storage.GetAsync(GetPath(feedSlug, name, version, ReadmeFileName), cancellationToken);
    }

    public async Task DeleteAsync(string feedSlug, string name, string version, CancellationToken cancellationToken)
    {
        await _storage.DeleteAsync(GetPath(feedSlug, name, version, ArchiveFileName), cancellationToken);
        await _storage.DeleteAsync(GetPath(feedSlug, name, version, ReadmeFileName), cancellationToken);
    }

    public async Task SaveFileAsync(string feedSlug, string name, string version, string fileName, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await ReplaceAsync(GetFilePath(feedSlug, name, version, fileName), content, ArchiveContentType, cancellationToken);
    }

    public async Task<Stream> GetFileStreamAsync(string feedSlug, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        return await _storage.GetAsync(GetFilePath(feedSlug, name, version, fileName), cancellationToken);
    }

    public async Task DeleteFileAsync(string feedSlug, string name, string version, string fileName, CancellationToken cancellationToken)
    {
        await _storage.DeleteAsync(GetFilePath(feedSlug, name, version, fileName), cancellationToken);
    }

    private async Task ReplaceAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var result = await _storage.PutAsync(path, content, contentType, cancellationToken);
        if (result == StoragePutResult.Conflict)
        {
            // A replaced file. Storage providers don't overwrite, so the old content goes first.
            await _storage.DeleteAsync(path, cancellationToken);

            content.Position = 0;
            result = await _storage.PutAsync(path, content, contentType, cancellationToken);
            if (result == StoragePutResult.Conflict)
            {
                throw new InvalidOperationException($"Failed to store {path} due to conflict");
            }
        }
    }

    private static string GetFilePath(string feedSlug, string name, string version, string fileName)
    {
        return GetPath(feedSlug, name, version, Path.Combine(FilesFolderName, fileName));
    }

    private static string GetPath(string feedSlug, string name, string version, string fileName)
    {
        // A Maven name is groupId:artifactId; file systems such as NTFS don't allow the colon.
        var folder = name.ToLowerInvariant().Replace(':', '/');

        return Path.Combine(PackagesPathPrefix, feedSlug, folder, version.ToLowerInvariant(), fileName);
    }
}
