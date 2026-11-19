using System.ComponentModel.DataAnnotations;

namespace PaGetto.Core.Configuration;

/// <summary>
/// Refusing mirrored versions that the OpenSSF malicious packages data lists, from a local copy of
/// the <c>MAL-</c> advisories of the OSV database.
/// </summary>
public class MaliciousPackageOptions
{
    public const string DefaultDataUrl = "https://osv-vulnerabilities.storage.googleapis.com/";

    /// <summary>
    /// Whether feeds refuse listed versions from their mirrors, unless a feed overrides it, and
    /// whether the list is refreshed. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The URL of the OSV data export, with a folder per ecosystem that holds <c>all.zip</c>,
    /// <c>modified_id.csv</c> and an <c>{id}.json</c> per advisory. Default is the OSV bucket,
    /// <c>https://osv-vulnerabilities.storage.googleapis.com/</c>.
    /// </summary>
    [Url]
    public string DataUrl { get; set; } = DefaultDataUrl;

    /// <summary>
    /// How many hours after its last refresh the list is refreshed again. Default is 1.
    /// </summary>
    [Range(1, 168)]
    public int RefreshIntervalHours { get; set; } = 1;

    /// <summary>
    /// How long a download from <see cref="DataUrl"/> may take, in seconds; the first copy of the
    /// npm advisories is a download of over 200 MB. Default is 900.
    /// </summary>
    [Range(1, 7200)]
    public int TimeoutSeconds { get; set; } = 900;
}
