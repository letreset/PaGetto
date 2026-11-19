using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Vulnerabilities;

/// <summary>
/// An advisory of the OSV database, in the parts PaGetto keeps
/// (see <see href="https://ossf.github.io/osv-schema/"/>).
/// </summary>
public class OsvAdvisory
{
    /// <summary>
    /// The longest text kept of a summary, the aliases or the affected versions.
    /// </summary>
    public const int MaxTextLength = 1000;

    private const int MaxVersionLength = 128;

    private static readonly string[] EventKinds = ["introduced", "fixed", "last_affected", "limit"];

    private readonly List<Affected> _affected = [];
    private List<string> _cvssVectors = [];
    private string _databaseSeverity;

    public string Id { get; private set; }

    /// <summary>
    /// The advisory's other ids, such as its CVE.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; private set; } = [];

    /// <summary>
    /// The summary, or the first line of the details when it has none.
    /// </summary>
    public string Summary { get; private set; }

    /// <summary>
    /// Whether the advisory was withdrawn; it then affects nothing.
    /// </summary>
    public bool Withdrawn { get; private set; }

    /// <summary>
    /// Reads an advisory as <c>GET /v1/vulns/{id}</c> returns it.
    /// </summary>
    public static OsvAdvisory Parse(JsonElement json)
    {
        var advisory = new OsvAdvisory
        {
            Id = GetString(json, "id"),
            Aliases = GetStrings(json, "aliases"),
            Withdrawn = GetString(json, "withdrawn") != null,
            _cvssVectors = GetCvssVectors(json),
            _databaseSeverity = json.TryGetProperty("database_specific", out var specific)
                ? GetString(specific, "severity")
                : null,
        };

        var summary = GetString(json, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = GetString(json, "details")?.Trim().Split('\n')[0].Trim();
        }

        advisory.Summary = Truncate(string.IsNullOrWhiteSpace(summary) ? null : summary);

        if (json.TryGetProperty("affected", out var affected) && affected.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in affected.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("package", out var package)
                    || package.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                advisory._affected.Add(new Affected(
                    GetString(package, "ecosystem"),
                    GetString(package, "name"),
                    GetRanges(entry),
                    GetStrings(entry, "versions"),
                    GetCvssVectors(entry)));
            }
        }

        return advisory;
    }

    /// <summary>
    /// What the advisory says about one package version: its severity, the affected versions and
    /// the version that fixes it.
    /// </summary>
    /// <param name="ecosystem">The OSV ecosystem of the package.</param>
    /// <param name="name">The package name as OSV knows it.</param>
    /// <param name="version">The version.</param>
    /// <param name="versionComparer">Orders the ecosystem's versions, to pick the fixed version to move to.</param>
    /// <returns>The finding, without its package and feed, or null when the advisory doesn't affect the package.</returns>
    public PackageVulnerability Describe(string ecosystem, string name, string version, IComparer<string> versionComparer)
    {
        ArgumentNullException.ThrowIfNull(versionComparer);

        var entries = _affected
            .Where(a => a.Ecosystem != null
                && (a.Ecosystem == ecosystem || a.Ecosystem.StartsWith(ecosystem + ":", StringComparison.Ordinal))
                && OsvEcosystem.NamesMatch(ecosystem, a.Name, name))
            .ToList();
        if (entries.Count == 0 || Withdrawn || string.IsNullOrEmpty(Id))
        {
            return null;
        }

        double? score = null;
        foreach (var vector in _cvssVectors.Concat(entries.SelectMany(e => e.CvssVectors)))
        {
            if (CvssV3.TryGetBaseScore(vector, out var s) && (score == null || s > score))
            {
                score = s;
            }
        }

        // The CVSS v3 score first, since it means the same in every ecosystem; the database's own
        // rating (GitHub's) for advisories without one, e.g. those with only a CVSS v4 vector.
        var severity = score.HasValue ? CvssV3.GetSeverity(score.Value) : ParseSeverity(_databaseSeverity);

        var fixedVersion = entries
            .SelectMany(e => e.Ranges)
            .SelectMany(r => r)
            .Where(e => e.Kind == "fixed" && e.Version.Length <= MaxVersionLength && Compare(versionComparer, e.Version, version) > 0)
            .Select(e => e.Version)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, Comparer<string>.Create((a, b) => Compare(versionComparer, a, b)))
            .FirstOrDefault();

        return new PackageVulnerability
        {
            AdvisoryId = Id,
            Aliases = Truncate(Aliases.Count == 0 ? null : string.Join(", ", Aliases)),
            Summary = Summary,
            Severity = severity,
            CvssScore = score,
            AffectedVersions = Truncate(DescribeRanges(entries)),
            FixedVersion = fixedVersion,
        };
    }

    // The ranges as one version range in npm's syntax, which reads well for every ecosystem:
    // ">=1.0.0 <1.2.5 || >=2.0.0 <2.0.3". Advisories without ranges list their versions.
    private static string DescribeRanges(List<Affected> entries)
    {
        var parts = new List<string>();
        foreach (var range in entries.SelectMany(e => e.Ranges))
        {
            string introduced = null;
            foreach (var (kind, version) in range)
            {
                switch (kind)
                {
                    case "introduced":
                        introduced = version;
                        break;
                    case "fixed":
                    case "limit":
                        parts.Add(Bounded(introduced, "<" + version));
                        introduced = null;
                        break;
                    case "last_affected":
                        parts.Add(Bounded(introduced, "<=" + version));
                        introduced = null;
                        break;
                }
            }

            if (introduced != null)
            {
                parts.Add(">=" + introduced);
            }
        }

        if (parts.Count == 0)
        {
            parts.AddRange(entries.SelectMany(e => e.Versions));
        }

        return parts.Count == 0 ? null : string.Join(" || ", parts.Distinct(StringComparer.Ordinal));
    }

    private static string Bounded(string introduced, string upper)
    {
        return introduced == null || introduced == "0" ? upper : $">={introduced} {upper}";
    }

    private static VulnerabilitySeverity ParseSeverity(string value)
    {
        return value?.ToUpperInvariant() switch
        {
            "CRITICAL" => VulnerabilitySeverity.Critical,
            "HIGH" => VulnerabilitySeverity.High,
            "MODERATE" or "MEDIUM" => VulnerabilitySeverity.Moderate,
            "LOW" => VulnerabilitySeverity.Low,
            _ => VulnerabilitySeverity.Unknown,
        };
    }

    private static int Compare(IComparer<string> comparer, string a, string b)
    {
        try
        {
            return comparer.Compare(a, b);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return string.CompareOrdinal(a, b);
        }
    }

    private static List<List<(string Kind, string Version)>> GetRanges(JsonElement entry)
    {
        var ranges = new List<List<(string Kind, string Version)>>();
        if (!entry.TryGetProperty("ranges", out var rangesJson) || rangesJson.ValueKind != JsonValueKind.Array)
        {
            return ranges;
        }

        foreach (var range in rangesJson.EnumerateArray())
        {
            // GIT ranges are commits, not versions.
            if (GetString(range, "type") == "GIT"
                || !range.TryGetProperty("events", out var events)
                || events.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var list = new List<(string Kind, string Version)>();
            foreach (var e in events.EnumerateArray())
            {
                foreach (var kind in EventKinds)
                {
                    var version = GetString(e, kind);
                    if (version != null)
                    {
                        list.Add((kind, version));
                    }
                }
            }

            ranges.Add(list);
        }

        return ranges;
    }

    private static List<string> GetCvssVectors(JsonElement json)
    {
        var vectors = new List<string>();
        if (json.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in severity.EnumerateArray())
            {
                var score = GetString(entry, "score");
                if (score != null && GetString(entry, "type") == "CVSS_V3")
                {
                    vectors.Add(score);
                }
            }
        }

        return vectors;
    }

    private static string GetString(JsonElement json, string name)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static List<string> GetStrings(JsonElement json, string name)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).ToList()
            : [];
    }

    private static string Truncate(string text)
    {
        return text == null || text.Length <= MaxTextLength ? text : string.Concat(text.AsSpan(0, MaxTextLength - 1), "…");
    }

    private sealed record Affected(
        string Ecosystem,
        string Name,
        List<List<(string Kind, string Version)>> Ranges,
        List<string> Versions,
        List<string> CvssVectors);
}
