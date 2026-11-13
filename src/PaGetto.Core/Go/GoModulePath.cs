using System;
using System.Globalization;
using System.Text;

namespace PaGetto.Core.Go;

/// <summary>
/// Go module paths, such as <c>example.com/team/tool/v2</c>. Paths are case-sensitive, so in URLs
/// and file names each uppercase letter is escaped as <c>!</c> and the lowercase letter. A module
/// of a Go feed is stored under its escaped path, which is unique regardless of case.
/// See: https://go.dev/ref/mod#module-path and https://go.dev/ref/mod#goproxy-protocol
/// </summary>
public static class GoModulePath
{
    /// <summary>
    /// The longest escaped path accepted. It matches the database column.
    /// </summary>
    public const int MaxEscapedLength = 128;

    private const string GopkgInPrefix = "gopkg.in/";

    // The names Windows reserves, which no path element may start with.
    private static readonly string[] _reservedWindowsNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Checks a module path the way the go command does: elements of ASCII letters, digits and
    /// <c>-._~</c>, a first element of lowercase letters, digits, <c>-</c> and <c>.</c> with at
    /// least one dot, and a valid major version suffix. Its escaped form must also fit the database.
    /// </summary>
    public static bool IsValid(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.EndsWith('/'))
        {
            return false;
        }

        var elements = path.Split('/');
        foreach (var element in elements)
        {
            if (!IsValidElement(element))
            {
                return false;
            }
        }

        var first = elements[0];
        if (!first.Contains('.', StringComparison.Ordinal) || first[0] == '-')
        {
            return false;
        }

        foreach (var c in first)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.'))
            {
                return false;
            }
        }

        return TrySplitMajorSuffix(path, out _) && Escape(path).Length <= MaxEscapedLength;
    }

    /// <summary>
    /// Escapes a path or version for URLs and storage: each uppercase letter becomes <c>!</c> and
    /// the lowercase letter, e.g. <c>github.com/!azure/sdk</c> for <c>github.com/Azure/sdk</c>.
    /// </summary>
    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z')
            {
                builder.Append('!').Append((char)(c + ('a' - 'A')));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>. An escaped text has no uppercase letters, and each <c>!</c>
    /// is followed by a lowercase letter.
    /// </summary>
    public static bool TryUnescape(string escaped, out string text)
    {
        text = null;
        if (escaped == null)
        {
            return false;
        }

        var builder = new StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            var c = escaped[i];
            if (c is >= 'A' and <= 'Z')
            {
                return false;
            }

            if (c == '!')
            {
                if (i + 1 >= escaped.Length || escaped[i + 1] is < 'a' or > 'z')
                {
                    return false;
                }

                builder.Append((char)(escaped[++i] - ('a' - 'A')));
            }
            else
            {
                builder.Append(c);
            }
        }

        text = builder.ToString();
        return true;
    }

    /// <summary>
    /// Checks that a version can be a version of a module: a module path ending with <c>/vN</c>
    /// has versions of major version N, any other path versions of major version 0 or 1.
    /// <c>gopkg.in</c> paths, which have their own rules, are not checked.
    /// </summary>
    /// <returns>Why the version does not fit the path, or null if it does.</returns>
    public static string CheckMajorVersion(string path, GoVersion version)
    {
        if (path.StartsWith(GopkgInPrefix, StringComparison.Ordinal) || !TrySplitMajorSuffix(path, out var major))
        {
            return null;
        }

        if (version.IsIncompatible)
        {
            return $"A +incompatible version is for a module without a go.mod file, but the zip of {path} must have one.";
        }

        if (major == null)
        {
            return version.Major <= 1
                ? null
                : $"The module path {path} has no /v{version.Major} suffix, so its versions must be v0 or v1.";
        }

        return version.Major.ToString(CultureInfo.InvariantCulture) == major
            ? null
            : $"The module path {path} ends with /v{major}, so its versions must be v{major}.";
    }

    /// <summary>
    /// Finds the major version suffix of a path, as the go command does.
    /// </summary>
    /// <param name="path">The module path.</param>
    /// <param name="major">The major version of a <c>/vN</c> suffix (N of 2 or more), or null if there is none.</param>
    /// <returns>False if the path ends with an invalid suffix such as <c>/v1</c>, <c>/v01</c> or <c>/v2.0</c>.</returns>
    private static bool TrySplitMajorSuffix(string path, out string major)
    {
        major = null;
        if (path.StartsWith(GopkgInPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        var i = path.Length;
        var hasDot = false;
        while (i > 0 && (path[i - 1] is (>= '0' and <= '9') or '.'))
        {
            hasDot |= path[i - 1] == '.';
            i--;
        }

        if (i <= 1 || i == path.Length || path[i - 1] != 'v' || path[i - 2] != '/')
        {
            return true;
        }

        var suffix = path[i..];
        if (hasDot || suffix[0] == '0' || suffix == "1")
        {
            return false;
        }

        major = suffix;
        return true;
    }

    private static bool IsValidElement(string element)
    {
        if (element.Length == 0 || element.Trim('.').Length == 0 || element[0] == '.' || element[^1] == '.')
        {
            return false;
        }

        foreach (var c in element)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~'))
            {
                return false;
            }
        }

        var shortName = element.Split('.')[0];
        foreach (var reserved in _reservedWindowsNames)
        {
            if (string.Equals(shortName, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // A name that looks like a Windows short file name, such as GOPROX~1.
        var tilde = shortName.LastIndexOf('~');
        if (tilde >= 0 && tilde < shortName.Length - 1)
        {
            var allDigits = true;
            foreach (var c in shortName[(tilde + 1)..])
            {
                allDigits &= c is >= '0' and <= '9';
            }

            if (allDigits)
            {
                return false;
            }
        }

        return true;
    }
}
