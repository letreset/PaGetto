namespace PaGetto.Core.Go;

/// <summary>
/// A <c>require</c> directive of a <c>go.mod</c> file.
/// </summary>
/// <param name="Path">The required module's path.</param>
/// <param name="Version">The minimum version.</param>
/// <param name="Indirect">Whether the requirement is marked <c>// indirect</c>.</param>
public sealed record GoRequirement(string Path, string Version, bool Indirect);
