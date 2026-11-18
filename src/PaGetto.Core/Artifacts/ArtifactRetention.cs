using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// The retention limits of a feed applied to the versions of one of its packages. The major,
/// minor and patch of a version are its first three numbers (<c>v1.2.3</c>, <c>1.2-3</c> and
/// <c>1.2</c>, which is 1.2.0, all qualify); a version that doesn't start with a number, such as
/// Composer's <c>dev-main</c>, is never removed. Prereleases are those the ecosystem calls
/// prereleases.
/// </summary>
public static partial class ArtifactRetention
{
    /// <summary>
    /// Whether any limit is set.
    /// </summary>
    public static bool HasLimits(RetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.MaxMajorVersions.HasValue
            || options.MaxMinorVersions.HasValue
            || options.MaxPatchVersions.HasValue
            || options.MaxPrereleaseVersions.HasValue
            || options.DeletePrereleasesOfOlderMajors;
    }

    /// <summary>
    /// The versions beyond the limits, which are to be removed. The newest version is always kept.
    /// </summary>
    /// <param name="feedType">The type of the package's feed, which orders its versions.</param>
    /// <param name="versions">All versions of the package.</param>
    /// <param name="options">The limits.</param>
    public static IReadOnlyList<ArtifactVersion> GetVersionsToRemove(
        FeedType feedType,
        IReadOnlyCollection<ArtifactVersion> versions,
        RetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(options);

        if (versions.Count < 2 || !HasLimits(options))
        {
            return [];
        }

        var numbered = versions
            .Select(v => (Version: v, Number: Parse(v.Version)))
            .Where(v => v.Number != null)
            .Select(v => new Numbered(v.Version, v.Number.Value))
            .ToList();

        var kept = numbered.ToHashSet();

        if (options.MaxMajorVersions is { } maxMajor)
        {
            kept = KeepNewest(kept, v => 0L, v => v.Number.Major, (int)maxMajor);
        }

        if (options.MaxMinorVersions is { } maxMinor)
        {
            kept = KeepNewest(kept, v => v.Number.Major, v => v.Number.Minor, (int)maxMinor);
        }

        if (options.MaxPatchVersions is { } maxPatch)
        {
            kept = KeepNewest(kept, v => (v.Number.Major, v.Number.Minor), v => v.Number.Patch, (int)maxPatch);
        }

        if (options.MaxPrereleaseVersions is { } maxPrerelease)
        {
            // The newest prereleases of each patch, in the ecosystem's version order.
            var comparer = ArtifactVersionOrder.GetComparer(feedType);
            var prereleases = kept
                .Where(v => v.Version.IsPrerelease)
                .GroupBy(v => v.Number)
                .SelectMany(g => g.OrderByDescending(v => v.Version.Version, comparer).Skip((int)maxPrerelease))
                .ToList();

            kept.ExceptWith(prereleases);
        }

        if (options.DeletePrereleasesOfOlderMajors)
        {
            // Without a stable version nothing is dropped.
            var latestStableMajor = numbered
                .Where(v => !v.Version.IsPrerelease)
                .Select(v => v.Number.Major)
                .DefaultIfEmpty(-1)
                .Max();

            kept.RemoveWhere(v => v.Version.IsPrerelease && v.Number.Major < latestStableMajor);
        }

        // A limit of 0 would leave the package without versions.
        var newest = ArtifactVersionOrder.SortDescending(feedType, versions)[0];

        return numbered
            .Where(v => !kept.Contains(v) && v.Version != newest)
            .Select(v => v.Version)
            .ToList();
    }

    /// <summary>
    /// The versions whose <paramref name="child"/> number is among the <paramref name="max"/>
    /// highest of their <paramref name="parent"/>.
    /// </summary>
    private static HashSet<Numbered> KeepNewest<TParent>(
        HashSet<Numbered> versions,
        Func<Numbered, TParent> parent,
        Func<Numbered, long> child,
        int max)
    {
        return versions
            .GroupBy(parent)
            .SelectMany(group =>
            {
                var newest = group.Select(child).Distinct().OrderByDescending(n => n).Take(max).ToHashSet();
                return group.Where(v => newest.Contains(child(v)));
            })
            .ToHashSet();
    }

    private static VersionNumber? Parse(string version)
    {
        var match = NumberRegex().Match(version ?? string.Empty);
        if (!match.Success
            || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return null;
        }

        return new VersionNumber(major, Part(match.Groups[2]), Part(match.Groups[3]));
    }

    private static long Part(System.Text.RegularExpressions.Group group)
    {
        return group.Success && long.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    [GeneratedRegex(@"^[vV]?(\d{1,18})(?:[.\-_](\d{1,18}))?(?:[.\-_](\d{1,18}))?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    private readonly record struct VersionNumber(long Major, long Minor, long Patch);

    private sealed record Numbered(ArtifactVersion Version, VersionNumber Number);
}
