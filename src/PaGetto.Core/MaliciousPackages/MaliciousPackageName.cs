using PaGetto.Core.Entities;
using PaGetto.Core.PyPI;
using PaGetto.Core.Vulnerabilities;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// The names malicious packages are stored and looked up by.
/// </summary>
public static class MaliciousPackageName
{
    /// <summary>
    /// The name of a package as it is stored and looked up: PEP 503 normalized for PyPI, lowercased
    /// for every other ecosystem.
    /// </summary>
    public static string Normalize(string ecosystem, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return ecosystem == "PyPI" ? PyPIPackageName.Normalize(name) : name.ToLowerInvariant();
    }

    /// <summary>
    /// The name a package of a feed has in the OSV data, or null when it can't be mapped.
    /// </summary>
    /// <param name="feedType">The type of the feed.</param>
    /// <param name="storedName">The name the package is stored under.</param>
    public static string GetOsvName(FeedType feedType, string storedName)
    {
        return OsvEcosystem.GetPackageName(feedType, storedName);
    }
}
