using System;
using System.Linq;
using System.Text.RegularExpressions;
using PaGetto.Core.Maven;

namespace PaGetto.Core.Ivy;

/// <summary>
/// A path in the Ivy repository layout that sbt publishes with <c>Resolver.ivyStylePatterns</c>,
/// below the repository's URL:
/// <c>{organisation}/{module}[/scala_{scalaVersion}][/sbt_{sbtVersion}]/{revision}/{type}s/{artifact}[-{classifier}].{ext}</c>,
/// e.g. <c>com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/jars/sbt-tool.jar</c>, or the checksum of
/// such a file. The organisation keeps its dots. The folder of a module (the path up to the
/// revision) is a directory listing of its revisions, which sbt reads for dynamic revisions such as
/// <c>1.+</c>. Branches are not supported.
/// </summary>
public sealed partial class IvyPath
{
    /// <summary>
    /// The path of the descriptor of a revision, below the revision's folder.
    /// </summary>
    public const string DescriptorFileName = "ivys/ivy.xml";

    private const string ScalaPrefix = "scala_";
    private const string SbtPrefix = "sbt_";

    // The lengths of the database columns.
    private const int MaxNameLength = 128;
    private const int MaxRevisionLength = 128;
    private const int MaxFileNameLength = 256;

    private IvyPath()
    {
    }

    /// <summary>
    /// Whether the path is the folder of a module, which lists its revisions, rather than a file.
    /// </summary>
    public bool IsDirectory { get; private init; }

    /// <summary>
    /// The organisation, e.g. <c>com.example</c>, as it is in the path.
    /// </summary>
    public string Organisation { get; private init; }

    /// <summary>
    /// The module, e.g. <c>sbt-tool</c>, as it is in the path.
    /// </summary>
    public string Module { get; private init; }

    /// <summary>
    /// The Scala version of an sbt plugin, e.g. <c>2.12</c>, or null.
    /// </summary>
    public string ScalaVersion { get; private init; }

    /// <summary>
    /// The sbt version of an sbt plugin, e.g. <c>1.0</c>, or null.
    /// </summary>
    public string SbtVersion { get; private init; }

    /// <summary>
    /// The revision, e.g. <c>1.0.0</c>. Null for the folder of a module.
    /// </summary>
    public string Revision { get; private init; }

    /// <summary>
    /// The file below the revision's folder, e.g. <c>jars/sbt-tool.jar</c> or <c>ivys/ivy.xml</c>,
    /// or the file whose checksum is requested. It is the name the file is stored under. Null for the
    /// folder of a module.
    /// </summary>
    public string FileName { get; private init; }

    /// <summary>
    /// The checksum requested instead of the file itself, or null for the file.
    /// </summary>
    public MavenChecksum? Checksum { get; private init; }

    /// <summary>
    /// The path of the file, without the extension of a checksum, or of the module's folder.
    /// </summary>
    public string FilePath { get; private init; }

    /// <summary>
    /// Whether the file is the descriptor of its revision, <c>ivys/ivy.xml</c>.
    /// </summary>
    public bool IsDescriptor => string.Equals(FileName, DescriptorFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The package name the module is stored under (see <see cref="GetPackageName"/>).
    /// </summary>
    public string PackageName => GetPackageName(Organisation, Module, ScalaVersion, SbtVersion);

    /// <summary>
    /// The package name of a module, lowercased: <c>organisation:module</c>, followed by
    /// <c>:scala_{version}</c> and <c>:sbt_{version}</c> for an sbt plugin, since a plugin is
    /// published once per sbt version under the same revision.
    /// </summary>
    public static string GetPackageName(string organisation, string module, string scalaVersion, string sbtVersion)
    {
        return GetDisplayName(organisation, module, scalaVersion, sbtVersion).ToLowerInvariant();
    }

    /// <summary>
    /// The name of a module in the form of <see cref="GetPackageName"/>, in the case it was published.
    /// </summary>
    public static string GetDisplayName(string organisation, string module, string scalaVersion, string sbtVersion)
    {
        var name = $"{organisation}:{module}";
        if (scalaVersion != null)
        {
            name += $":{ScalaPrefix}{scalaVersion}";
        }

        if (sbtVersion != null)
        {
            name += $":{SbtPrefix}{sbtVersion}";
        }

        return name;
    }

    /// <summary>
    /// The path of a module's folder, which lists its revisions.
    /// </summary>
    public static string GetModulePath(string organisation, string module, string scalaVersion, string sbtVersion)
    {
        var path = $"{organisation}/{module}";
        if (scalaVersion != null)
        {
            path += $"/{ScalaPrefix}{scalaVersion}";
        }

        if (sbtVersion != null)
        {
            path += $"/{SbtPrefix}{sbtVersion}";
        }

        return path;
    }

    /// <summary>
    /// The path of a file of a revision, below the revision's folder, e.g. <c>jars/sbt-tool.jar</c>.
    /// </summary>
    public static string GetFilePath(string organisation, string module, string scalaVersion, string sbtVersion, string revision, string fileName)
    {
        return $"{GetModulePath(organisation, module, scalaVersion, sbtVersion)}/{revision}/{fileName}";
    }

    /// <summary>
    /// Reads a path of the repository layout.
    /// </summary>
    /// <returns>False if the path is not a file or module folder of this layout that the server can store.</returns>
    public static bool TryParse(string path, out IvyPath result)
    {
        result = null;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Clients ask for a folder with a trailing slash.
        var isFolder = path.EndsWith('/');
        var segments = path.TrimEnd('/').Split('/');
        if (segments.Length < 2 || segments.Any(s => !SegmentRegex().IsMatch(s)))
        {
            return false;
        }

        var organisation = segments[0];
        var module = segments[1];
        var next = 2;
        var scalaVersion = TakeVersion(segments, ref next, ScalaPrefix);
        var sbtVersion = TakeVersion(segments, ref next, SbtPrefix);

        if (GetPackageName(organisation, module, scalaVersion, sbtVersion).Length > MaxNameLength)
        {
            return false;
        }

        var rest = segments[next..];
        if (rest.Length == 0)
        {
            result = new IvyPath
            {
                IsDirectory = true,
                Organisation = organisation,
                Module = module,
                ScalaVersion = scalaVersion,
                SbtVersion = sbtVersion,
                FilePath = GetModulePath(organisation, module, scalaVersion, sbtVersion),
            };

            return true;
        }

        if (isFolder || rest.Length != 3)
        {
            return false;
        }

        var revision = rest[0];
        var typeFolder = rest[1];
        var fileName = rest[2];

        MavenChecksum? checksum = null;
        if (MavenChecksums.TryParse(fileName, out var parsedChecksum, out var checkedFileName))
        {
            checksum = parsedChecksum;
            fileName = checkedFileName;
        }

        // The type folder is the artifact's type with an s, e.g. jars, srcs, docs or ivys. A
        // revision that looks like a cross-version folder would make the module's folder ambiguous.
        var storedName = $"{typeFolder}/{fileName}";
        if (typeFolder.Length < 2
            || !typeFolder.EndsWith('s')
            || !fileName.Contains('.', StringComparison.Ordinal)
            || storedName.Length > MaxFileNameLength
            || revision.Length > MaxRevisionLength
            || revision.StartsWith('.')
            || IsCrossVersionFolder(revision))
        {
            return false;
        }

        result = new IvyPath
        {
            Organisation = organisation,
            Module = module,
            ScalaVersion = scalaVersion,
            SbtVersion = sbtVersion,
            Revision = revision,
            FileName = storedName,
            Checksum = checksum,
            FilePath = GetFilePath(organisation, module, scalaVersion, sbtVersion, revision, storedName),
        };

        return true;
    }

    /// <summary>
    /// Whether a folder name is one of the cross-version folders of an sbt plugin, which are not
    /// revisions.
    /// </summary>
    public static bool IsCrossVersionFolder(string name)
    {
        return name.StartsWith(ScalaPrefix, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(SbtPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string TakeVersion(string[] segments, ref int next, string prefix)
    {
        if (next < segments.Length
            && segments[next].Length > prefix.Length
            && segments[next].StartsWith(prefix, StringComparison.Ordinal))
        {
            return segments[next++][prefix.Length..];
        }

        return null;
    }

    // Letters, digits and the punctuation of Ivy names and revisions; not "." or "..".
    [GeneratedRegex(@"^(?!\.{1,2}$)[A-Za-z0-9_.+\-]+$")]
    private static partial Regex SegmentRegex();
}
