using System;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Entities;

/// <summary>
/// An upstream package source mirrored by a feed. A feed can have several mirrors, which are
/// queried in <see cref="SortOrder"/> order.
/// </summary>
public class FeedMirror
{
    public int Id { get; set; }
    public Guid FeedId { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; }
    public string PackageSource { get; set; }
    public bool Legacy { get; set; }
    public int? DownloadTimeoutSeconds { get; set; }
    public MirrorAuthenticationType? AuthType { get; set; }
    public string AuthUsername { get; set; }
    public string AuthPassword { get; set; }
    public string AuthToken { get; set; }
    public string AuthCustomHeaders { get; set; }

    public Feed Feed { get; set; }
}
