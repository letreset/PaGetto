using System;
using System.Collections.Generic;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Go;
using PaGetto.Core.PyPI;

namespace PaGetto.Core.Vulnerabilities;

/// <summary>
/// The OSV ecosystem of each feed type, and the name OSV knows a stored package by.
/// </summary>
public static class OsvEcosystem
{
    /// <summary>
    /// The feed types OSV has advisories for. CocoaPods, Swift (OSV knows Swift packages by
    /// repository URL) and VSIX feeds are not scanned.
    /// </summary>
    public static readonly IReadOnlyList<FeedType> SupportedFeedTypes =
    [
        FeedType.NuGet,
        FeedType.Pub,
        FeedType.Npm,
        FeedType.Maven,
        FeedType.PyPI,
        FeedType.Cargo,
        FeedType.Go,
        FeedType.RubyGems,
        FeedType.Hex,
        FeedType.Composer,
        FeedType.Cran,
        FeedType.Ivy,
    ];

    /// <summary>
    /// The OSV ecosystem of a feed type, or null when OSV has none.
    /// </summary>
    public static string GetName(FeedType feedType)
    {
        return feedType switch
        {
            FeedType.NuGet => "NuGet",
            FeedType.Pub => "Pub",
            FeedType.Npm => "npm",
            FeedType.Maven or FeedType.Ivy => "Maven",
            FeedType.PyPI => "PyPI",
            FeedType.Cargo => "crates.io",
            FeedType.Go => "Go",
            FeedType.RubyGems => "RubyGems",
            FeedType.Hex => "Hex",
            FeedType.Composer => "Packagist",
            FeedType.Cran => "CRAN",
            _ => null,
        };
    }

    /// <summary>
    /// The name OSV knows a stored package by.
    /// </summary>
    /// <param name="feedType">The type of the package's feed.</param>
    /// <param name="storedName">The name the package is stored under.</param>
    /// <param name="manifestJson">The version's manifest; only read for CRAN packages.</param>
    /// <returns>The name, or null when the stored name can't be mapped.</returns>
    public static string GetPackageName(FeedType feedType, string storedName, string manifestJson = null)
    {
        switch (feedType)
        {
            case FeedType.Go:
                // Stored under the escaped path; OSV has the path itself.
                return GoModulePath.TryUnescape(storedName, out var path) ? path : null;

            case FeedType.Ivy:
                // organisation:module[:scala_{v}][:sbt_{v}]; Maven coordinates are the first two.
                var parts = storedName.Split(':');
                return parts.Length >= 2 ? parts[0] + ":" + parts[1] : null;

            case FeedType.Cran:
                // Stored lowercased; CRAN names are case-sensitive and OSV has the published case.
                var published = CranManifest.GetName(manifestJson);
                return string.IsNullOrEmpty(published) ? storedName : published;

            default:
                return storedName;
        }
    }

    /// <summary>
    /// Whether a package name of an advisory is the package that was looked up.
    /// </summary>
    public static bool NamesMatch(string ecosystem, string advisoryName, string name)
    {
        if (advisoryName == null || name == null)
        {
            return false;
        }

        if (ecosystem == "PyPI")
        {
            return PyPIPackageName.Normalize(advisoryName) == PyPIPackageName.Normalize(name);
        }

        return string.Equals(advisoryName, name, StringComparison.OrdinalIgnoreCase);
    }
}
