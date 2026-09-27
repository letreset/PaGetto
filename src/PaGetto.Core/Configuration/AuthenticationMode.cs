using System;

namespace PaGetto.Core.Configuration;

/// <summary>
/// Controls which authentication mechanisms are active. <c>Authentication:Mode</c> is required:
/// the default value (0) is not a mode, so startup validation rejects a missing setting.
/// </summary>
public enum AuthenticationMode
{
    /// <summary>
    /// Only Entra ID (OIDC) authentication is enabled. Local accounts are not accepted.
    /// </summary>
    Entra = 1,

    /// <summary>
    /// Only local account authentication is enabled. Entra ID is not available.
    /// </summary>
    Local = 2,

    /// <summary>
    /// Both Entra ID and local account authentication are enabled.
    /// </summary>
    Hybrid = 3,

    /// <summary>
    /// Legacy config-file-based API keys and basic auth credentials (<c>ApiKey</c>,
    /// <c>Authentication:ApiKeys</c>, <c>Authentication:Credentials</c>). No database-backed users,
    /// no feed permissions and no admin pages.
    /// </summary>
    Legacy = 4,

    /// <summary>
    /// The old name of <see cref="Legacy"/>, still accepted in configuration.
    /// </summary>
    [Obsolete("Use Legacy.")]
    Config = Legacy,
}
