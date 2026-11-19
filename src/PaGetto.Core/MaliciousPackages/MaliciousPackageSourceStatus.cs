using System;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// The state of the local copy of one ecosystem's malicious packages.
/// </summary>
/// <param name="Ecosystem">The OSV ecosystem.</param>
/// <param name="Needed">Whether a mirroring feed blocks the ecosystem's malicious packages, so the copy is refreshed.</param>
/// <param name="Count">How many packages the copy lists.</param>
/// <param name="RefreshedAtUtc">When the copy was last refreshed, or null if it never was.</param>
/// <param name="LastError">Why the last refresh failed, or null if it succeeded.</param>
public record MaliciousPackageSourceStatus(string Ecosystem, bool Needed, int Count, DateTime? RefreshedAtUtc, string LastError);
