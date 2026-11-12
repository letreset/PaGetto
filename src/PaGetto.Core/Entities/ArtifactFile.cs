using System;

namespace PaGetto.Core.Entities;

/// <summary>
/// One file of an <see cref="ArtifactVersion"/>, for ecosystems whose versions consist of several
/// files, such as the POM, JAR and sources of a Maven version. Ecosystems with a single archive per
/// version (pub, npm) don't use it.
/// </summary>
public class ArtifactFile
{
    public int Key { get; set; }

    public int ArtifactVersionKey { get; set; }
    public ArtifactVersion ArtifactVersion { get; set; }

    /// <summary>
    /// The file name as it was published, unique within the version.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// The length of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The lowercase hex SHA-1 of the file.
    /// </summary>
    public string Sha1 { get; set; }

    /// <summary>
    /// The lowercase hex MD5 of the file.
    /// </summary>
    public string Md5 { get; set; }

    /// <summary>
    /// The lowercase hex SHA-256 of the file.
    /// </summary>
    public string Sha256 { get; set; }

    /// <summary>
    /// The lowercase hex SHA-512 of the file.
    /// </summary>
    public string Sha512 { get; set; }

    /// <summary>
    /// When the file was stored, or last replaced.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
