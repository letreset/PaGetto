using System;
using System.Collections.Generic;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The platform a VSIX file of a VS Code extension is built for, the <c>TargetPlatform</c> of its
/// manifest's <c>Identity</c>. A version can have one file for every platform, or one file without
/// a platform, which is <see cref="Universal"/>.
/// See: https://code.visualstudio.com/api/working-with-extensions/publishing-extension#platformspecific-extensions
/// </summary>
public static class VsCodeTargetPlatform
{
    /// <summary>
    /// The platform of a file built for every platform.
    /// </summary>
    public const string Universal = "universal";

    private static readonly HashSet<string> _platforms = new(StringComparer.Ordinal)
    {
        Universal,
        "win32-x64",
        "win32-arm64",
        "win32-ia32",
        "linux-x64",
        "linux-arm64",
        "linux-armhf",
        "alpine-x64",
        "alpine-arm64",
        "darwin-x64",
        "darwin-arm64",
        "web",
    };

    /// <summary>
    /// Reads a platform: a known one, lowercased, or <see cref="Universal"/> when none is given.
    /// </summary>
    /// <returns>False if the platform is not known.</returns>
    public static bool TryParse(string text, out string platform)
    {
        platform = string.IsNullOrEmpty(text) ? Universal : text.ToLowerInvariant();
        if (_platforms.Contains(platform))
        {
            return true;
        }

        platform = null;
        return false;
    }
}
