using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Storage;

/// <summary>
/// Stores content on disk.
/// </summary>
public class FileStorageService : IStorageService
{
    private const int DefaultCopyBufferSize = 81920;

    private readonly string _storePath;

    public FileStorageService(IOptionsSnapshot<FileSystemStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Resolve relative path components ('.'/'..') and ensure there is a trailing slash.
        _storePath = Path.GetFullPath(options.Value.Path);
        if (!_storePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            _storePath += Path.DirectorySeparatorChar;
    }

    public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);
        var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult<Stream>(content);
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Uri(GetFullPath(path));

        return Task.FromResult(result);
    }

    public async Task<StoragePutResult> PutAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type is required", nameof(contentType));

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);

        // Ensure that the path exists.
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        try
        {
            using var fileStream = File.Open(path, FileMode.CreateNew);
            await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            return StoragePutResult.Success;
        }
        catch (IOException) when (File.Exists(path))
        {
            using var targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            content.Position = 0;
            return content.Matches(targetStream)
                ? StoragePutResult.AlreadyExists
                : StoragePutResult.Conflict;
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = GetFullPath(path);

        try
        {
            File.Delete(fullPath);
        }
        catch (DirectoryNotFoundException)
        {
        }

        DeleteEmptyParentDirectories(fullPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes empty directories left behind by a delete, walking up from the file's directory.
    /// Stops before the top-level namespace folder (e.g. "packages" or "symbols") directly under the store path.
    /// </summary>
    private void DeleteEmptyParentDirectories(string filePath)
    {
        var storePath = Path.TrimEndingDirectorySeparator(_storePath);
        var directory = Directory.GetParent(filePath);

        // Only directories strictly below the top-level namespace folder are candidates.
        while (directory?.Parent != null &&
               directory.FullName.StartsWith(_storePath, StringComparison.Ordinal) &&
               !PathsEqual(directory.Parent.FullName, storePath))
        {
            if (directory.Exists && directory.EnumerateFileSystemInfos().Any())
            {
                break;
            }

            try
            {
                directory.Delete(recursive: false);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
                // Directory became non-empty or is in use; leave it.
                break;
            }

            directory = directory.Parent;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private string GetFullPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Path is required", nameof(path));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_storePath, path));

        // Verify path is under the _storePath.
        if (!fullPath.StartsWith(_storePath, StringComparison.Ordinal) ||
            fullPath.Length == _storePath.Length)
        {
            throw new ArgumentException("Path resolves outside store path", nameof(path));
        }

        return fullPath;
    }
}
