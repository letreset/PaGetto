using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;

namespace PaGetto.Web.Models;

/// <summary>
/// Safe projection of a <see cref="FeedMirror"/> for API responses. Secret fields (password,
/// token, custom headers) are replaced with boolean presence indicators.
/// </summary>
public class FeedMirrorResponse
{
    public int SortOrder { get; set; }
    public bool Enabled { get; set; }
    public string PackageSource { get; set; }
    public bool Legacy { get; set; }
    public int? DownloadTimeoutSeconds { get; set; }
    public MirrorAuthenticationType? AuthType { get; set; }
    public string AuthUsername { get; set; }

    /// <summary>True if a password is configured; the value is never returned.</summary>
    public bool HasAuthPassword { get; set; }

    /// <summary>True if a bearer token is configured; the value is never returned.</summary>
    public bool HasAuthToken { get; set; }

    public static FeedMirrorResponse FromMirror(FeedMirror mirror) => new FeedMirrorResponse
    {
        SortOrder = mirror.SortOrder,
        Enabled = mirror.Enabled,
        PackageSource = mirror.PackageSource,
        Legacy = mirror.Legacy,
        DownloadTimeoutSeconds = mirror.DownloadTimeoutSeconds,
        AuthType = mirror.AuthType,
        AuthUsername = mirror.AuthUsername,
        HasAuthPassword = !string.IsNullOrEmpty(mirror.AuthPassword),
        HasAuthToken = !string.IsNullOrEmpty(mirror.AuthToken),
    };
}
