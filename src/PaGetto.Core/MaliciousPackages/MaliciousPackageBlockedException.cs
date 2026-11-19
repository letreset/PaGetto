using System;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// A version was not mirrored or served because the malicious packages data lists it. The web
/// host answers it in the format of the feed's client.
/// </summary>
public class MaliciousPackageBlockedException : Exception
{
    public MaliciousPackageBlockedException(string feedSlug, string packageName, string version, string advisoryId, string summary)
        : base($"{packageName} {version} is blocked: it is listed as a malicious package ({advisoryId}). An administrator can allow it if this is a false positive.")
    {
        FeedSlug = feedSlug;
        PackageName = packageName;
        Version = version;
        AdvisoryId = advisoryId;
        Summary = summary;
    }

    public string FeedSlug { get; }

    /// <summary>
    /// The package, as the malicious packages data names it.
    /// </summary>
    public string PackageName { get; }

    public string Version { get; }

    /// <summary>
    /// The advisory that lists the version, e.g. <c>MAL-2024-1234</c>.
    /// </summary>
    public string AdvisoryId { get; }

    public string Summary { get; }
}
