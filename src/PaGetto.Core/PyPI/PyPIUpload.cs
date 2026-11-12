using System.IO;

namespace PaGetto.Core.PyPI;

/// <summary>
/// A file uploaded with the legacy upload API, as <c>twine upload</c> and <c>uv publish</c> send it.
/// </summary>
public sealed class PyPIUpload
{
    /// <summary>
    /// The project name of the form (<c>name</c>).
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The version of the form (<c>version</c>), or null if it has none.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The name of the uploaded file (the <c>content</c> part).
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// The content of the uploaded file.
    /// </summary>
    public Stream Content { get; init; }

    /// <summary>
    /// The hex SHA-256 the client computed (<c>sha256_digest</c>), or null.
    /// </summary>
    public string Sha256Digest { get; init; }

    /// <summary>
    /// The hex MD5 the client computed (<c>md5_digest</c>), or null.
    /// </summary>
    public string Md5Digest { get; init; }
}
