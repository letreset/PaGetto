using System;
using NuGet.Versioning;

namespace PaGetto.Core.Updates;

/// <summary>
/// A published PaGetto release.
/// </summary>
public class ReleaseInfo
{
    public ReleaseInfo(NuGetVersion version, Uri releaseNotesUrl)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        ReleaseNotesUrl = releaseNotesUrl ?? throw new ArgumentNullException(nameof(releaseNotesUrl));
    }

    public NuGetVersion Version { get; }

    /// <summary>
    /// The release's page on GitHub, with its release notes.
    /// </summary>
    public Uri ReleaseNotesUrl { get; }
}
