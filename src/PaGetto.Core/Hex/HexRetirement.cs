using System;
using System.Text.Json.Serialization;

namespace PaGetto.Core.Hex;

/// <summary>
/// Why a Hex package version was retired. A retired version is unlisted: clients only resolve it
/// when a project has it locked already.
/// </summary>
public sealed class HexRetirement
{
    /// <summary>
    /// The reasons, at the index of their value in the registry.
    /// </summary>
    private static readonly string[] _reasons = ["other", "invalid", "security", "deprecated", "renamed"];

    /// <summary>
    /// One of <c>other</c>, <c>invalid</c>, <c>security</c>, <c>deprecated</c> and <c>renamed</c>.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; }

    public static bool IsValidReason(string reason)
    {
        return Array.IndexOf(_reasons, reason) >= 0;
    }

    /// <summary>
    /// The registry's value of a reason. An unknown reason is <c>other</c>.
    /// </summary>
    public static int GetReasonCode(string reason)
    {
        return Math.Max(0, Array.IndexOf(_reasons, reason));
    }

    /// <summary>
    /// The reason of a registry value. An unknown value is <c>other</c>.
    /// </summary>
    public static string GetReason(ulong code)
    {
        return code < (ulong)_reasons.Length ? _reasons[code] : _reasons[0];
    }
}
