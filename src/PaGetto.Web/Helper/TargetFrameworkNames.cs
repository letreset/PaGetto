using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;

namespace PaGetto.Web.Helper;

/// <summary>
/// Readable names and a stable order for target framework monikers (<c>net8.0</c>, <c>net48</c>, ...).
/// </summary>
public static class TargetFrameworkNames
{
    /// <summary>
    /// Returns a display name such as ".NET 8.0", ".NET Core 3.1", ".NET Standard 2.0",
    /// ".NET Framework 4.7.2" or ".NET Portable (net45, win8)", or the moniker itself when it can't be parsed.
    /// </summary>
    public static string GetDisplayName(string moniker)
    {
        if (moniker == null) return "All Frameworks";

        var portableName = TryGetPortableDisplayName(moniker);
        if (portableName != null) return portableName;

        var framework = TryParse(moniker);
        if (framework == null) return moniker;

        var frameworkVersion = (framework.Version.Build == 0)
            ? framework.Version.ToString(2)
            : framework.Version.ToString(3);

        var name = $"{GetFamilyName(framework)} {frameworkVersion}";
        return framework.HasPlatform ? $"{name} ({framework.Platform})" : name;
    }

    /// <summary>
    /// Orders monikers by family (.NET, .NET Core, .NET Standard, .NET Framework, others), then by
    /// version, newest first. Monikers that can't be parsed come last, in string order.
    /// </summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string> monikers)
    {
        return monikers
            .Select(m => (Moniker: m, Framework: TryParse(m)))
            .OrderBy(x => x.Framework == null ? int.MaxValue : GetFamilyRank(x.Framework))
            .ThenByDescending(x => x.Framework?.Version)
            .ThenBy(x => x.Moniker, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Moniker)
            .ToList();
    }

    /// <summary>
    /// Returns the lowest framework per family as display names (e.g. ".NET 6.0", ".NET Standard 2.0"),
    /// in family order. A package targeting a framework is compatible with that framework or higher,
    /// so these are the package's badges. <c>any</c> and monikers that can't be parsed are skipped.
    /// </summary>
    public static IReadOnlyList<string> GetLowestPerFamily(IEnumerable<string> monikers)
    {
        return monikers
            .Select(TryParse)
            .Where(f => f != null && !f.IsAny && !f.IsAgnostic)
            .GroupBy(GetFamilyName)
            .Select(group => group
                .OrderBy(f => f.Version)
                .ThenBy(f => f.HasPlatform)
                .First())
            .OrderBy(GetFamilyRank)
            .ThenBy(f => f.Framework, StringComparer.OrdinalIgnoreCase)
            .Select(f => GetDisplayName(f.GetShortFolderName()))
            .ToList();
    }

    private static NuGetFramework TryParse(string moniker)
    {
        try
        {
            var framework = NuGetFramework.Parse(moniker);
            return framework.IsUnsupported || framework.IsPCL ? null : framework;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Names a portable class library profile after the frameworks it targets, in string order.
    /// Returns null when the moniker isn't a PCL or its profile is unknown.
    /// </summary>
    private static string TryGetPortableDisplayName(string moniker)
    {
        try
        {
            var framework = NuGetFramework.Parse(moniker);
            if (!framework.IsPCL
                || !DefaultFrameworkNameProvider.Instance.TryGetPortableFrameworks(framework.Profile, includeOptional: false, out var frameworks))
            {
                return null;
            }

            var names = frameworks
                .Select(f => f.GetShortFolderName())
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            return $".NET Portable ({string.Join(", ", names)})";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int GetFamilyRank(NuGetFramework framework)
    {
        return GetFamilyName(framework) switch
        {
            ".NET" => 0,
            ".NET Core" => 1,
            ".NET Standard" => 2,
            ".NET Framework" => 3,
            _ => 4,
        };
    }

    private static string GetFamilyName(NuGetFramework framework)
    {
        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.OrdinalIgnoreCase))
        {
            return framework.Version.Major >= 5 ? ".NET" : ".NET Core";
        }

        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetStandard, StringComparison.OrdinalIgnoreCase))
        {
            return ".NET Standard";
        }

        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.Net, StringComparison.OrdinalIgnoreCase))
        {
            return ".NET Framework";
        }

        return framework.Framework;
    }
}
