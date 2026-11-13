using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The full name of a gem file: <c>{name}-{version}</c> for the default platform (<c>ruby</c>),
/// <c>{name}-{version}-{platform}</c> for another, as in <c>nokogiri-1.16.0-x86_64-linux.gem</c>.
/// A name can have dashes too, so a full name is parsed into every name and version it can be.
/// </summary>
public sealed partial class RubyGemsFileName
{
    /// <summary>
    /// The platform of a gem of pure Ruby, which its file name leaves out.
    /// </summary>
    public const string RubyPlatform = "ruby";

    /// <summary>
    /// The extension of a gem file.
    /// </summary>
    public const string GemExtension = ".gem";

    /// <summary>
    /// The extension of a gemspec as <c>gem install</c> reads it: Ruby Marshal data, compressed.
    /// </summary>
    public const string QuickSpecExtension = ".gemspec.rz";

    // The longest platform accepted, e.g. universal-darwin-23 or x86_64-linux-musl.
    private const int MaxPlatformLength = 64;

    private RubyGemsFileName(string name, string version, string platform)
    {
        Name = name;
        Version = version;
        Platform = platform;
    }

    public string Name { get; }
    public string Version { get; }

    /// <summary>
    /// The platform, <see cref="RubyPlatform"/> for a gem of pure Ruby.
    /// </summary>
    public string Platform { get; }

    public string FullName => GetFullName(Name, Version, Platform);

    /// <summary>
    /// The full name of a gem: its name, version and platform, without an extension.
    /// </summary>
    public static string GetFullName(string name, string version, string platform)
    {
        return IsRubyPlatform(platform) ? $"{name}-{version}" : $"{name}-{version}-{platform}";
    }

    /// <summary>
    /// Whether a platform is the default one. A missing platform is the default one too.
    /// </summary>
    public static bool IsRubyPlatform(string platform)
    {
        return string.IsNullOrEmpty(platform) || platform == RubyPlatform;
    }

    /// <summary>
    /// Whether a platform is one a file name can have: letters, digits, '.', '_' and '-', starting
    /// with a letter, as in <c>x86_64-linux</c>, <c>java</c> or <c>arm64-darwin-23</c>.
    /// </summary>
    public static bool IsValidPlatform(string platform)
    {
        return platform != null && platform.Length <= MaxPlatformLength && PlatformPattern().IsMatch(platform);
    }

    /// <summary>
    /// Every way a full name splits into a valid gem name, version and platform: at each dash
    /// followed by a digit, the name ends and the version starts; the version ends at the next dash.
    /// </summary>
    /// <param name="fullName">The full name, without its extension.</param>
    /// <returns>The splits, the shortest name first. Empty if there are none.</returns>
    public static IReadOnlyList<RubyGemsFileName> Parse(string fullName)
    {
        var result = new List<RubyGemsFileName>();
        if (string.IsNullOrEmpty(fullName))
        {
            return result;
        }

        for (var dash = fullName.IndexOf('-'); dash > 0; dash = fullName.IndexOf('-', dash + 1))
        {
            if (dash + 1 >= fullName.Length || !char.IsAsciiDigit(fullName[dash + 1]))
            {
                continue;
            }

            var name = fullName[..dash];
            var rest = fullName[(dash + 1)..];
            var platformDash = rest.IndexOf('-');
            var version = platformDash < 0 ? rest : rest[..platformDash];
            var platform = platformDash < 0 ? RubyPlatform : rest[(platformDash + 1)..];

            if (RubyGemsPackageName.IsValid(name)
                && RubyGemsVersion.TryParse(version, out _)
                && (platformDash < 0 || (IsValidPlatform(platform) && !IsRubyPlatform(platform))))
            {
                result.Add(new RubyGemsFileName(name, version, platform));
            }
        }

        return result;
    }

    /// <summary>
    /// Removes an extension from a file name.
    /// </summary>
    /// <returns>The full name, or null if the file name does not end with the extension.</returns>
    public static string TrimExtension(string fileName, string extension)
    {
        return fileName != null && fileName.EndsWith(extension, StringComparison.Ordinal) && fileName.Length > extension.Length
            ? fileName[..^extension.Length]
            : null;
    }

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9._\-]*\z")]
    private static partial Regex PlatformPattern();
}
