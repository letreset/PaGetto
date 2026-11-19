namespace PaGetto.Core.Sbom;

/// <summary>
/// A dependency a package version declares in its manifest: a package of the same ecosystem and
/// the versions it accepts.
/// </summary>
/// <param name="Name">The name of the package depended on, as the manifest gives it.</param>
/// <param name="Requirement">
/// The requirement as the package page shows it, e.g. <c>^1.2.0</c> or <c>1.0 · test</c>, or null.
/// </param>
public sealed record SbomDependency(string Name, string Requirement);
