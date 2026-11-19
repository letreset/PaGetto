namespace PaGetto.Core.Vulnerabilities;

/// <summary>
/// One package version to look up in the OSV database.
/// </summary>
/// <param name="Ecosystem">The OSV ecosystem, e.g. <c>npm</c> (see <see cref="OsvEcosystem"/>).</param>
/// <param name="Name">The package name as OSV knows it.</param>
/// <param name="Version">The version.</param>
public record OsvQuery(string Ecosystem, string Name, string Version);
