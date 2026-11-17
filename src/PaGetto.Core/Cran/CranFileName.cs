using System;

namespace PaGetto.Core.Cran;

/// <summary>
/// The file name of a source package, <c>{name}_{version}.tar.gz</c>, which <c>install.packages</c>
/// builds from the name and version of the index. Neither a name nor a version has an underscore.
/// </summary>
public static class CranFileName
{
    public const string Extension = ".tar.gz";

    public static string Create(string name, string version)
    {
        return $"{name}_{version}{Extension}";
    }

    /// <summary>
    /// Reads the name and version of a file name.
    /// </summary>
    /// <returns>False if the file name is not that of a source package with a valid name and version.</returns>
    public static bool TryParse(string fileName, out string name, out CranVersion version)
    {
        name = null;
        version = null;

        if (fileName == null || !fileName.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = fileName[..^Extension.Length].Split('_');
        if (parts.Length != 2 || !CranPackageName.IsValid(parts[0]) || !CranVersion.TryParse(parts[1], out version))
        {
            version = null;
            return false;
        }

        name = parts[0];
        return true;
    }
}
