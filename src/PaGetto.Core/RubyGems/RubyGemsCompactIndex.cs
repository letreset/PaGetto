using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The text of the compact index, which Bundler and RubyGems read: <c>/versions</c> lists every gem
/// with its versions and the MD5 of its info file, and <c>/info/{gem}</c> has one line per gem file
/// with its runtime dependencies, checksum and required Ruby and RubyGems versions.
/// See: https://guides.rubygems.org/rubygems-org-compact-index-api/
/// </summary>
public static class RubyGemsCompactIndex
{
    /// <summary>
    /// The line that ends the header of an index file.
    /// </summary>
    public const string HeaderEnd = "---";

    /// <summary>
    /// The info line of a gem file: <c>{version}[-{platform}] {dependencies}|checksum:{sha256}[,ruby:…][,rubygems:…]</c>,
    /// where each dependency is <c>{name}:{requirement}</c> and requirements join their parts with <c>&amp;</c>.
    /// </summary>
    public static string InfoLine(RubyGemsSpecification specification)
    {
        var line = new StringBuilder();
        line.Append(VersionEntry(specification.Version, specification.Platform)).Append(' ');

        line.AppendJoin(',', specification.Dependencies
            .Where(d => d.Type == RubyGemsDependency.RuntimeType)
            .Select(d => $"{d.Name}:{Requirement(d.Requirement)}"));

        line.Append("|checksum:").Append(specification.Sha256);

        if (specification.RequiredRubyVersion.Count > 0)
        {
            line.Append(",ruby:").Append(Requirement(specification.RequiredRubyVersion));
        }

        if (specification.RequiredRubygemsVersion.Count > 0)
        {
            line.Append(",rubygems:").Append(Requirement(specification.RequiredRubygemsVersion));
        }

        return line.ToString();
    }

    /// <summary>
    /// An info file: the header, then one line per gem file.
    /// </summary>
    public static string InfoFile(IEnumerable<string> lines)
    {
        var file = new StringBuilder(HeaderEnd).Append('\n');
        foreach (var line in lines)
        {
            file.Append(line).Append('\n');
        }

        return file.ToString();
    }

    /// <summary>
    /// The header of <c>/versions</c>.
    /// </summary>
    public static string VersionsHeader(DateTime createdAtUtc)
    {
        return $"created_at: {createdAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\n{HeaderEnd}\n";
    }

    /// <summary>
    /// The line of a gem in <c>/versions</c>: its name, its versions, and the MD5 of its info file.
    /// </summary>
    /// <param name="name">The gem name as published.</param>
    /// <param name="versions">The versions, see <see cref="VersionEntry"/>; one with a leading <c>-</c> is removed.</param>
    /// <param name="infoChecksum">The lowercase hex MD5 of the gem's info file.</param>
    public static string VersionsLine(string name, IEnumerable<string> versions, string infoChecksum)
    {
        return $"{name} {string.Join(',', versions)} {infoChecksum}\n";
    }

    /// <summary>
    /// A version as the compact index names it: <c>{version}</c>, or <c>{version}-{platform}</c>
    /// for a platform other than <c>ruby</c>.
    /// </summary>
    public static string VersionEntry(string version, string platform)
    {
        return RubyGemsFileName.IsRubyPlatform(platform) ? version : $"{version}-{platform}";
    }

    /// <summary>
    /// The lowercase hex MD5 of a file, which <c>/versions</c> lists for each info file and which
    /// is the ETag of both.
    /// </summary>
    public static string Md5(byte[] content)
    {
#pragma warning disable CA5351 // MD5 is what the compact index uses, not for security.
        return Convert.ToHexStringLower(MD5.HashData(content));
#pragma warning restore CA5351
    }

    /// <summary>
    /// Splits the start of an info line, <c>{version}[-{platform}]</c>, into its version and platform.
    /// </summary>
    /// <returns>False if the line does not start with a version.</returns>
    public static bool TryParseVersionEntry(string line, out string version, out string platform)
    {
        var entry = line.Split(' ', 2)[0];
        var dash = entry.IndexOf('-');
        version = dash < 0 ? entry : entry[..dash];
        platform = dash < 0 ? RubyGemsFileName.RubyPlatform : entry[(dash + 1)..];

        return RubyGemsVersion.TryParse(version, out _) && (dash < 0 || RubyGemsFileName.IsValidPlatform(platform));
    }

    /// <summary>
    /// The checksum of an info line, from its requirements after the <c>|</c>.
    /// </summary>
    /// <returns>The lowercase hex SHA-256, or null if the line has none.</returns>
    public static string GetChecksum(string line)
    {
        var bar = line.LastIndexOf('|');
        if (bar < 0)
        {
            return null;
        }

        foreach (var requirement in line[(bar + 1)..].Split(','))
        {
            if (requirement.StartsWith("checksum:", StringComparison.Ordinal))
            {
                return requirement["checksum:".Length..].Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    private static string Requirement(List<string> requirement)
    {
        return requirement.Count == 0 ? ">= 0" : string.Join('&', requirement);
    }
}
