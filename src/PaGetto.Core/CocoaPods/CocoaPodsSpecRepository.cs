using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// The files of a CocoaPods spec repository in the CDN format, which CocoaPods reads file by file
/// since a CDN can't list folders: <c>CocoaPods-version.yml</c>, an index of the pods and their
/// versions per shard (<c>all_pods_versions_d_a_2.txt</c>), and a podspec per version
/// (<c>Specs/d/a/2/Alamofire/5.9.1/Alamofire.podspec.json</c>).
/// See: https://github.com/CocoaPods/Core/blob/master/lib/cocoapods-core/cdn_source.rb
/// </summary>
public static partial class CocoaPodsSpecRepository
{
    public const string VersionFileName = "CocoaPods-version.yml";
    public const string AllPodsFileName = "all_pods.txt";
    public const string DeprecatedPodspecsFileName = "deprecated_podspecs.txt";
    public const string PodspecExtension = ".podspec.json";

    /// <summary>
    /// The folder of the podspecs.
    /// </summary>
    public const string SpecsFolder = "Specs";

    /// <summary>
    /// <c>CocoaPods-version.yml</c>: the CocoaPods versions that can read the repository, and the
    /// prefix lengths of its shards. CocoaPods takes a source URL for a CDN repository when this
    /// file has a <c>last</c> version, and tells its users about a newer CocoaPods when <c>last</c>
    /// is newer than theirs; this server can't know the newest one, so it names the first version
    /// that reads repositories in this format.
    /// </summary>
    public const string VersionFile = "---\nmin: 1.8.0\nlast: 1.8.0\nprefix_lengths:\n- 1\n- 1\n- 1\n";

    /// <summary>
    /// The name of the index file of a shard.
    /// </summary>
    public static string GetIndexFileName(IReadOnlyList<string> shard)
    {
        return $"all_pods_versions_{string.Join('_', shard)}.txt";
    }

    /// <summary>
    /// Reads the shard from the name of an index file.
    /// </summary>
    public static bool TryParseIndexFileName(string fileName, out string[] shard)
    {
        var match = IndexFileNameRegex().Match(fileName ?? "");
        shard = match.Success ? [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value] : null;
        return match.Success;
    }

    /// <summary>
    /// The path of a version's podspec below the repository's URL, unescaped.
    /// </summary>
    public static string GetPodspecPath(string name, string version)
    {
        return $"{SpecsFolder}/{string.Join('/', CocoaPodsPodName.GetShard(name))}/{name}/{version}/{name}{PodspecExtension}";
    }

    /// <summary>
    /// Writes an index file: a line per pod, of the name and its versions separated by slashes.
    /// </summary>
    public static string WriteIndex(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> pods)
    {
        var builder = new StringBuilder();
        foreach (var (name, versions) in pods.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(name);
            foreach (var version in versions)
            {
                builder.Append('/').Append(version);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads an index file. Lines whose pod name or versions this server can't serve are left out.
    /// </summary>
    /// <returns>The versions of each pod, by its name.</returns>
    public static Dictionary<string, IReadOnlyList<string>> ReadIndex(string index)
    {
        var pods = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var line in index.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split('/');
            if (!CocoaPodsPodName.IsValid(parts[0]))
            {
                continue;
            }

            var versions = parts.Skip(1).Where(v => CocoaPodsVersion.TryParse(v, out _)).ToList();
            if (versions.Count > 0)
            {
                pods[parts[0]] = versions;
            }
        }

        return pods;
    }

    [GeneratedRegex("^all_pods_versions_([0-9a-f])_([0-9a-f])_([0-9a-f])\\.txt$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexFileNameRegex();
}
