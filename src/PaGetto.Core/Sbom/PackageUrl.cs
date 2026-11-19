using System;
using System.Text;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Sbom;

/// <summary>
/// The package URLs (purl) of packages, the identifiers SBOM tools and vulnerability databases
/// match packages by. See: https://github.com/package-url/purl-spec
/// </summary>
public static class PackageUrl
{
    /// <summary>
    /// The purl type of a feed type, or null when the purl specification has none (VSIX and VS Code
    /// extensions).
    /// </summary>
    public static string GetType(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.NuGet => "nuget",
            FeedType.Pub => "pub",
            FeedType.Npm => "npm",
            FeedType.Maven or FeedType.Ivy => "maven",
            FeedType.PyPI => "pypi",
            FeedType.Cargo => "cargo",
            FeedType.Go => "golang",
            FeedType.RubyGems => "gem",
            FeedType.Hex => "hex",
            FeedType.Composer => "composer",
            FeedType.CocoaPods => "cocoapods",
            FeedType.Swift => "swift",
            FeedType.Cran => "cran",
            _ => null,
        };
    }

    /// <summary>
    /// The package URL of a package, or of one of its versions.
    /// </summary>
    /// <param name="feedType">The ecosystem of the package.</param>
    /// <param name="name">The name as published.</param>
    /// <param name="version">The version, or null for the package.</param>
    /// <returns>The package URL, or null when the ecosystem has no purl type.</returns>
    public static string Create(FeedType feedType, string name, string version)
    {
        var type = GetType(feedType);
        if (type == null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        Split(feedType, name, out var ns, out var shortName, out var subpath);

        // npm, pub, Hex and Composer names are lowercase; Python names compare with '_' as '-'.
        if (feedType is FeedType.Npm or FeedType.Pub or FeedType.Hex or FeedType.Composer or FeedType.PyPI)
        {
            ns = ns?.ToLowerInvariant();
            shortName = shortName.ToLowerInvariant();
        }

        if (feedType == FeedType.PyPI)
        {
            shortName = shortName.Replace('_', '-');
        }

        var builder = new StringBuilder("pkg:").Append(type).Append('/');
        if (ns != null)
        {
            foreach (var segment in ns.Split('/'))
            {
                builder.Append(Uri.EscapeDataString(segment)).Append('/');
            }
        }

        builder.Append(Uri.EscapeDataString(shortName));

        if (!string.IsNullOrEmpty(version))
        {
            builder.Append('@').Append(Uri.EscapeDataString(version));
        }

        if (subpath != null)
        {
            builder.Append('#').Append(string.Join('/', Array.ConvertAll(subpath.Split('/'), Uri.EscapeDataString)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a name into the parts of a package URL: the namespace (a Maven group, an npm scope, a
    /// Composer vendor, the folder of a Go module or the scope of a Swift package), the name, and
    /// the subpath (a CocoaPods subspec).
    /// </summary>
    public static void Split(FeedType feedType, string name, out string ns, out string shortName, out string subpath)
    {
        ns = null;
        shortName = name;
        subpath = null;

        switch (feedType)
        {
            case FeedType.Maven:
            case FeedType.Ivy:
                // groupId:artifactId; an Ivy name may add :scala_{v} and :sbt_{v}.
                var parts = name.Split(':');
                if (parts.Length >= 2)
                {
                    ns = parts[0];
                    shortName = parts[1];
                }

                break;

            case FeedType.Npm:
                var scopeEnd = name.IndexOf('/', StringComparison.Ordinal);
                if (name.StartsWith('@') && scopeEnd > 0)
                {
                    ns = name[..scopeEnd];
                    shortName = name[(scopeEnd + 1)..];
                }

                break;

            case FeedType.Composer:
                SplitAt(name, name.IndexOf('/', StringComparison.Ordinal), ref ns, ref shortName);
                break;

            case FeedType.Go:
                SplitAt(name, name.LastIndexOf('/'), ref ns, ref shortName);
                break;

            case FeedType.Swift:
                SplitAt(name, name.IndexOf('.', StringComparison.Ordinal), ref ns, ref shortName);
                break;

            case FeedType.CocoaPods:
                var podEnd = name.IndexOf('/', StringComparison.Ordinal);
                if (podEnd > 0)
                {
                    shortName = name[..podEnd];
                    subpath = name[(podEnd + 1)..];
                }

                break;
        }
    }

    private static void SplitAt(string name, int index, ref string ns, ref string shortName)
    {
        if (index > 0 && index < name.Length - 1)
        {
            ns = name[..index];
            shortName = name[(index + 1)..];
        }
    }
}
