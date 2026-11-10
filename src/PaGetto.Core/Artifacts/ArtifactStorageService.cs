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

    private static string GetPath(string feedSlug, string name, string version, string fileName)
    {
        return Path.Combine(PackagesPathPrefix, feedSlug, name.ToLowerInvariant(), version.ToLowerInvariant(), fileName);
    }
}
