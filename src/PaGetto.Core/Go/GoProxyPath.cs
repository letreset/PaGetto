using System;

namespace PaGetto.Core.Go;

/// <summary>
/// A path of the GOPROXY protocol, relative to the proxy's URL: <c>{module}/@v/list</c>,
/// <c>{module}/@v/{version}.info</c>, <c>.mod</c> or <c>.zip</c>, or <c>{module}/@latest</c>, with
/// the module path and version escaped (see <see cref="GoModulePath.Escape"/>).
/// See: https://go.dev/ref/mod#goproxy-protocol
/// </summary>
public sealed class GoProxyPath
{
    private const string LatestSuffix = "/@latest";
    private const string VersionsSegment = "/@v/";

    public GoProxyRequest Request { get; private init; }

    /// <summary>
    /// The unescaped module path. It is not checked to be a valid module path.
    /// </summary>
    public string ModulePath { get; private init; }

    /// <summary>
    /// The unescaped version or version query of an <c>.info</c>, <c>.mod</c> or <c>.zip</c>
    /// request, or null for the others. It is not checked to be a canonical version.
    /// </summary>
    public string Version { get; private init; }

    public static bool TryParse(string path, out GoProxyPath result)
    {
        result = null;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string escapedModule;
        string escapedVersion = null;
        GoProxyRequest request;

        if (path.EndsWith(LatestSuffix, StringComparison.Ordinal))
        {
            escapedModule = path[..^LatestSuffix.Length];
            request = GoProxyRequest.Latest;
        }
        else
        {
            var versions = path.LastIndexOf(VersionsSegment, StringComparison.Ordinal);
            if (versions < 0)
            {
                return false;
            }

            escapedModule = path[..versions];
            var file = path[(versions + VersionsSegment.Length)..];

            if (file == "list")
            {
                request = GoProxyRequest.List;
            }
            else if (TrySplitExtension(file, ".info", out escapedVersion))
            {
                request = GoProxyRequest.Info;
            }
            else if (TrySplitExtension(file, ".mod", out escapedVersion))
            {
                request = GoProxyRequest.Mod;
            }
            else if (TrySplitExtension(file, ".zip", out escapedVersion))
            {
                request = GoProxyRequest.Zip;
            }
            else
            {
                return false;
            }

            if (escapedVersion != null && (escapedVersion.Length == 0 || escapedVersion.Contains('/', StringComparison.Ordinal)))
            {
                return false;
            }
        }

        if (escapedModule.Length == 0 || !GoModulePath.TryUnescape(escapedModule, out var modulePath))
        {
            return false;
        }

        string version = null;
        if (escapedVersion != null && !GoModulePath.TryUnescape(escapedVersion, out version))
        {
            return false;
        }

        result = new GoProxyPath { Request = request, ModulePath = modulePath, Version = version };
        return true;
    }

    private static bool TrySplitExtension(string file, string extension, out string name)
    {
        name = file.EndsWith(extension, StringComparison.Ordinal) ? file[..^extension.Length] : null;
        return name != null;
    }
}

/// <summary>
/// The requests of the GOPROXY protocol.
/// </summary>
public enum GoProxyRequest
{
    /// <summary>
    /// <c>@v/list</c>: the module's versions.
    /// </summary>
    List,

    /// <summary>
    /// <c>@latest</c>: the info of the module's latest version.
    /// </summary>
    Latest,

    /// <summary>
    /// <c>@v/{version}.info</c>: the version and its time.
    /// </summary>
    Info,

    /// <summary>
    /// <c>@v/{version}.mod</c>: the version's <c>go.mod</c> file.
    /// </summary>
    Mod,

    /// <summary>
    /// <c>@v/{version}.zip</c>: the version's files.
    /// </summary>
    Zip,
}
