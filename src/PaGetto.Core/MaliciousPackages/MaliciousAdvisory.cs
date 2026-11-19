using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// A malicious package advisory of the OSV data (an id that starts with <c>MAL-</c>), in the
/// parts PaGetto keeps.
/// </summary>
public class MaliciousAdvisory
{
    /// <summary>
    /// The prefix of the ids of malicious package advisories.
    /// </summary>
    public const string IdPrefix = "MAL-";

    private const int MaxSummaryLength = 1000;

    public string Id { get; private init; }

    public DateTime ModifiedUtc { get; private init; }

    public string Summary { get; private init; }

    /// <summary>
    /// Whether the advisory was withdrawn, e.g. as a false positive; it then lists nothing.
    /// </summary>
    public bool Withdrawn { get; private init; }

    /// <summary>
    /// The packages it lists: their ecosystem, name as the advisory has it, and affected versions
    /// as <see cref="MaliciousVersions.Read"/> stores them.
    /// </summary>
    public IReadOnlyList<(string Ecosystem, string Name, string AffectedVersions)> Packages { get; private init; } = [];

    /// <summary>
    /// Whether an id is that of a malicious package advisory.
    /// </summary>
    public static bool IsMaliciousId(string id)
    {
        return id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// An advisory that the data no longer has, which lists nothing.
    /// </summary>
    public static MaliciousAdvisory Removed(string id)
    {
        return new MaliciousAdvisory { Id = id, Withdrawn = true };
    }

    /// <summary>
    /// Reads an advisory in the OSV format.
    /// </summary>
    /// <returns>The advisory, or null when it is not a malicious package advisory.</returns>
    public static MaliciousAdvisory ParseOrNull(JsonElement json)
    {
        var id = GetString(json, "id");
        if (!IsMaliciousId(id))
        {
            return null;
        }

        var packages = new List<(string, string, string)>();
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

                // An ecosystem may have a suffix, e.g. "Debian:12"; the registries have none.
                var ecosystem = GetString(package, "ecosystem")?.Split(':')[0];
                var name = GetString(package, "name");
                if (!string.IsNullOrEmpty(ecosystem) && !string.IsNullOrEmpty(name))
                {
                    packages.Add((ecosystem, name, MaliciousVersions.Read(entry)));
                }
            }
        }

        var summary = GetString(json, "summary")?.Trim();
        if (summary?.Length > MaxSummaryLength)
        {
            summary = string.Concat(summary.AsSpan(0, MaxSummaryLength - 1), "…");
        }

        return new MaliciousAdvisory
        {
            Id = id,
            ModifiedUtc = TryParseTimestamp(GetString(json, "modified"), out var modified) ? modified : DateTime.MinValue,
            Summary = string.IsNullOrEmpty(summary) ? null : summary,
            Withdrawn = GetString(json, "withdrawn") != null,
            Packages = packages,
        };
    }

    /// <summary>
    /// Reads a timestamp of the OSV data, which has up to nine digits of fractional seconds.
    /// </summary>
    public static bool TryParseTimestamp(string text, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // .NET reads at most seven digits of fractional seconds.
        var dot = text.IndexOf('.');
        if (dot >= 0)
        {
            var end = dot + 1;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if (end - dot - 1 > 7)
            {
                text = string.Concat(text.AsSpan(0, dot + 8), text.AsSpan(end));
            }
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return false;
        }

        utc = parsed.UtcDateTime;
        return true;
    }

    private static string GetString(JsonElement json, string name)
    {
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
