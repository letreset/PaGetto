using System;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Maven;

/// <summary>
/// The parts of the name of a file of a Maven version:
/// <c>{artifactId}-{fileVersion}[-{classifier}].{extension}</c>, e.g. <c>my-lib-1.0-sources.jar</c>.
/// The file version of a snapshot is timestamped per build, e.g. <c>1.0-20240101.120000-3</c> for
/// <c>1.0-SNAPSHOT</c>.
/// </summary>
public sealed partial class MavenFileName
{
    private MavenFileName()
    {
    }

    /// <summary>
    /// The version in the file name: the version, or a timestamped snapshot version.
    /// </summary>
    public string FileVersion { get; private init; }

    /// <summary>
    /// The classifier, e.g. <c>sources</c>, or null for the main file of its extension.
    /// </summary>
    public string Classifier { get; private init; }

    /// <summary>
    /// The extension, e.g. <c>jar</c>, <c>pom</c> or <c>jar.asc</c>.
    /// </summary>
    public string Extension { get; private init; }

    /// <summary>
    /// The build timestamp of a snapshot file, e.g. <c>20240101.120000</c>, or null.
    /// </summary>
    public string SnapshotTimestamp { get; private init; }

    /// <summary>
    /// The build number of a snapshot file, or 0 when it is not timestamped.
    /// </summary>
    public int SnapshotBuildNumber { get; private init; }

    /// <summary>
    /// Whether this is the version's POM, which describes it.
    /// </summary>
    public bool IsPom => Classifier == null && string.Equals(Extension, "pom", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this is a signature of another file, e.g. <c>my-lib-1.0.jar.asc</c>.
    /// </summary>
    public bool IsSignature => Extension.EndsWith(".asc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Extension, "asc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the name of a file of a version.
    /// </summary>
    /// <returns>False if the name is not that of a file of this artifact and version.</returns>
    public static bool TryParse(string artifactId, string version, string fileName, out MavenFileName result)
    {
        result = null;

        var prefix = artifactId + "-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = fileName[prefix.Length..];
        string fileVersion;
        string timestamp = null;
        var buildNumber = 0;

        if (rest.StartsWith(version, StringComparison.OrdinalIgnoreCase))
        {
            fileVersion = rest[..version.Length];
        }
        else if (MavenVersion.IsSnapshot(version)
            && rest.StartsWith(MavenVersion.GetSnapshotBase(version), StringComparison.OrdinalIgnoreCase)
            && SnapshotBuildRegex().Match(rest[MavenVersion.GetSnapshotBase(version).Length..]) is { Success: true } build
            && int.TryParse(build.Groups["number"].ValueSpan, out buildNumber))
        {
            fileVersion = rest[..(MavenVersion.GetSnapshotBase(version).Length + build.Length)];
            timestamp = build.Groups["timestamp"].Value;
        }
        else
        {
            return false;
        }

        var tail = rest[fileVersion.Length..];
        string classifier = null;
        string extension;

        if (tail.StartsWith('-'))
        {
            var dot = tail.IndexOf('.');
            if (dot < 2 || dot == tail.Length - 1)
            {
                return false;
            }

            classifier = tail[1..dot];
            extension = tail[(dot + 1)..];
        }
        else if (tail.StartsWith('.') && tail.Length > 1)
        {
            extension = tail[1..];
        }
        else
        {
            return false;
        }

        result = new MavenFileName
        {
            FileVersion = fileVersion,
            Classifier = classifier,
            Extension = extension,
            SnapshotTimestamp = timestamp,
            SnapshotBuildNumber = buildNumber,
        };

        return true;
    }

    [GeneratedRegex(@"^(?<timestamp>\d{8}\.\d{6})-(?<number>\d{1,9})")]
    private static partial Regex SnapshotBuildRegex();
}
