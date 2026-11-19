using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Npm;

/// <summary>
/// Writes the answer to <c>npm audit</c>'s bulk advisory request from stored vulnerability findings:
/// for each package name, the advisories of its versions, with the range of versions they affect,
/// which npm matches against the versions it has installed.
/// </summary>
public static class NpmAuditAdvisories
{
    /// <summary>
    /// The link npm shows for an advisory.
    /// </summary>
    public const string AdvisoryUrlPrefix = "https://osv.dev/vulnerability/";

    /// <summary>
    /// Builds the answer.
    /// </summary>
    /// <param name="requestedNames">The names npm asked about, by the name they are stored under.</param>
    /// <param name="findings">The findings of the stored versions of those packages.</param>
    public static JsonObject Create(IReadOnlyDictionary<string, string> requestedNames, IEnumerable<PackageVulnerability> findings)
    {
        ArgumentNullException.ThrowIfNull(requestedNames);
        ArgumentNullException.ThrowIfNull(findings);

        var result = new JsonObject();
        foreach (var package in findings.GroupBy(f => f.PackageName, StringComparer.Ordinal))
        {
            if (!requestedNames.TryGetValue(package.Key, out var requested))
            {
                continue;
            }

            var advisories = new JsonArray();
            foreach (var finding in package.GroupBy(f => f.AdvisoryId, StringComparer.Ordinal).Select(g => g.First()))
            {
                advisories.Add(new JsonObject
                {
                    ["id"] = GetNumericId(finding.AdvisoryId),
                    ["url"] = AdvisoryUrlPrefix + Uri.EscapeDataString(finding.AdvisoryId),
                    ["title"] = finding.Summary ?? finding.AdvisoryId,
                    ["severity"] = GetSeverity(finding.Severity),
                    ["vulnerable_versions"] = finding.AffectedVersions ?? finding.PackageVersion,
                    ["cwe"] = new JsonArray(),
                    ["cvss"] = new JsonObject { ["score"] = finding.CvssScore ?? 0, ["vectorString"] = null },
                });
            }

            result[requested] = advisories;
        }

        return result;
    }

    /// <summary>
    /// npm's advisories have numeric ids; OSV's are text. The number is taken from the SHA-256 of
    /// the text, so an advisory keeps its number.
    /// </summary>
    public static int GetNumericId(string advisoryId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(advisoryId));
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) & int.MaxValue);
    }

    private static string GetSeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "critical",
            VulnerabilitySeverity.High => "high",
            VulnerabilitySeverity.Moderate => "moderate",
            VulnerabilitySeverity.Low => "low",
            _ => "info",
        };
    }
}
