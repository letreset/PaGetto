namespace PaGetto.Core.Configuration;

/// <summary>
/// Checking once a day whether a newer PaGetto release exists, to tell administrators about it.
/// </summary>
public class UpdateCheckOptions
{
    /// <summary>
    /// Whether the latest release is looked up on GitHub. Turn it off for servers without network
    /// access or that should not contact GitHub. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
