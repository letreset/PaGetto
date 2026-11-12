using System;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Maven;

/// <summary>
/// The file extensions and values of <see cref="MavenChecksum"/>.
/// </summary>
public static class MavenChecksums
{
    private static readonly (string Extension, MavenChecksum Checksum)[] Extensions =
    [
        (".sha1", MavenChecksum.Sha1),
        (".md5", MavenChecksum.Md5),
        (".sha256", MavenChecksum.Sha256),
        (".sha512", MavenChecksum.Sha512),
    ];

    /// <summary>
    /// Whether a file name is that of a checksum file, and of which file.
    /// </summary>
    /// <param name="fileName">The file name, e.g. <c>my-lib-1.0.jar.sha1</c>.</param>
    /// <param name="checksum">The checksum, e.g. SHA-1.</param>
    /// <param name="checkedFileName">The name of the file it is the checksum of, e.g. <c>my-lib-1.0.jar</c>.</param>
    public static bool TryParse(string fileName, out MavenChecksum checksum, out string checkedFileName)
    {
        foreach (var (extension, value) in Extensions)
        {
            if (fileName.Length > extension.Length && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                checksum = value;
                checkedFileName = fileName[..^extension.Length];
                return true;
            }
        }

        checksum = default;
        checkedFileName = null;
        return false;
    }

    /// <summary>
    /// The stored checksum of a file, as lowercase hex.
    /// </summary>
    public static string GetValue(ArtifactFile file, MavenChecksum checksum)
    {
        return checksum switch
        {
            MavenChecksum.Sha1 => file.Sha1,
            MavenChecksum.Md5 => file.Md5,
            MavenChecksum.Sha256 => file.Sha256,
            _ => file.Sha512,
        };
    }
}
