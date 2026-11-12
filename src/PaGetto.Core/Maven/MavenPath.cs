using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Maven;

/// <summary>
/// A path in the Maven repository layout, below the repository's URL:
/// <c>{groupPath}/{artifactId}/{version}/{fileName}</c> for a file of a version,
/// <c>{groupPath}/{artifactId}/maven-metadata.xml</c> for the versions of an artifact, and
/// <c>{groupPath}/{artifactId}/{version}/maven-metadata.xml</c> for the builds of a snapshot. The
/// group path is the group id with its dots as slashes. Each of these has checksum files next to it.
/// See: https://maven.apache.org/repositories/layout.html
/// </summary>
public sealed partial class MavenPath
{
    /// <summary>
    /// The name of the metadata files.
    /// </summary>
    public const string MetadataFileName = "maven-metadata.xml";

    // The lengths of the database columns.
    private const int MaxNameLength = 128;
    private const int MaxVersionLength = 128;
    private const int MaxFileNameLength = 256;

    private MavenPath()
    {
    }

    public MavenPathKind Kind { get; private init; }

    /// <summary>
    /// The group id, e.g. <c>com.example</c>, as it is in the path.
    /// </summary>
    public string GroupId { get; private init; }

    /// <summary>
    /// The artifact id, e.g. <c>my-lib</c>, as it is in the path.
    /// </summary>
    public string ArtifactId { get; private init; }

    /// <summary>
    /// The version, e.g. <c>1.0</c> or <c>1.0-SNAPSHOT</c>. Null for the metadata of an artifact.
    /// </summary>
    public string Version { get; private init; }

    /// <summary>
    /// The name of the file, or of the file whose checksum is requested.
    /// </summary>
    public string FileName { get; private init; }

    /// <summary>
    /// The parts of <see cref="FileName"/> for a file of a version. Null for metadata.
    /// </summary>
    public MavenFileName File { get; private init; }

    /// <summary>
    /// The checksum requested instead of the file itself, or null for the file.
    /// </summary>
    public MavenChecksum? Checksum { get; private init; }

    /// <summary>
    /// The path of the file, without the extension of a checksum.
    /// </summary>
    public string FilePath { get; private init; }

    /// <summary>
    /// The package name the artifact is stored under: <c>groupId:artifactId</c>, lowercased.
    /// </summary>
    public string PackageName => GetPackageName(GroupId, ArtifactId);

    /// <summary>
    /// The package name of an artifact: <c>groupId:artifactId</c>, lowercased.
    /// </summary>
    public static string GetPackageName(string groupId, string artifactId)
    {
        return $"{groupId}:{artifactId}".ToLowerInvariant();
    }

    /// <summary>
    /// The path of a file of a version.
    /// </summary>
    public static string GetFilePath(string groupId, string artifactId, string version, string fileName)
    {
        return $"{groupId.Replace('.', '/')}/{artifactId}/{version}/{fileName}";
    }

    /// <summary>
    /// Reads a path of the repository layout.
    /// </summary>
    /// <returns>False if the path is not a file or metadata of this layout that the server can store.</returns>
    public static bool TryParse(string path, out MavenPath result)
    {
        result = null;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Any(s => !SegmentRegex().IsMatch(s)))
        {
            return false;
        }

        var fileName = segments[^1];
        MavenChecksum? checksum = null;
        if (MavenChecksums.TryParse(fileName, out var parsedChecksum, out var checkedFileName))
        {
            checksum = parsedChecksum;
            fileName = checkedFileName;
        }

        var filePath = string.Join('/', segments[..^1].Append(fileName));

        if (fileName == MetadataFileName)
        {
            // The metadata of a snapshot version, which lists its builds.
            if (segments.Length >= 4 && MavenVersion.IsSnapshot(segments[^2]))
            {
                return TryCreate(MavenPathKind.SnapshotMetadata, segments[..^3], segments[^3], segments[^2], fileName, null, checksum, filePath, out result);
            }

            // The metadata of an artifact, which lists its versions. A group's metadata (the
            // prefixes of its Maven plugins) has the same form, with the last part of the group id
            // where the artifact id is.
            if (segments.Length >= 3)
            {
                return TryCreate(MavenPathKind.ArtifactMetadata, segments[..^2], segments[^2], null, fileName, null, checksum, filePath, out result);
            }

            return false;
        }

        if (segments.Length < 4
            || !MavenFileName.TryParse(segments[^3], segments[^2], fileName, out var file))
        {
            return false;
        }

        return TryCreate(MavenPathKind.File, segments[..^3], segments[^3], segments[^2], fileName, file, checksum, filePath, out result);
    }

    private static bool TryCreate(
        MavenPathKind kind,
        string[] groupSegments,
        string artifactId,
        string version,
        string fileName,
        MavenFileName file,
        MavenChecksum? checksum,
        string filePath,
        out MavenPath result)
    {
        result = null;

        // A dot in a group segment would make the group id ambiguous: a.b/c and a/b.c are both a.b.c.
        var groupId = string.Join('.', groupSegments);
        if (groupSegments.Length == 0
            || groupSegments.Any(s => s.Contains('.'))
            || GetPackageName(groupId, artifactId).Length > MaxNameLength
            || fileName.Length > MaxFileNameLength
            || (version != null && (version.Length > MaxVersionLength || version.StartsWith('.'))))
        {
            return false;
        }

        result = new MavenPath
        {
            Kind = kind,
            GroupId = groupId,
            ArtifactId = artifactId,
            Version = version,
            FileName = fileName,
            File = file,
            Checksum = checksum,
            FilePath = filePath,
        };

        return true;
    }

    // Letters, digits and the punctuation of Maven coordinates; not "." or "..".
    [GeneratedRegex(@"^(?!\.{1,2}$)[A-Za-z0-9_.+\-]+$")]
    private static partial Regex SegmentRegex();
}

/// <summary>
/// What a <see cref="MavenPath"/> points at.
/// </summary>
public enum MavenPathKind
{
    /// <summary>
    /// A file of a version, such as its POM or JAR.
    /// </summary>
    File,

    /// <summary>
    /// The <c>maven-metadata.xml</c> of an artifact (or a group), which lists its versions.
    /// </summary>
    ArtifactMetadata,

    /// <summary>
    /// The <c>maven-metadata.xml</c> of a snapshot version, which lists its builds.
    /// </summary>
    SnapshotMetadata,
}
