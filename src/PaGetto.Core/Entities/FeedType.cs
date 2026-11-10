namespace PaGetto.Core.Entities;

/// <summary>
/// The package ecosystem a feed serves. A feed speaks exactly one protocol, which is fixed when the
/// feed is created. The numeric values are stored in the database, so they must not change.
/// </summary>
public enum FeedType
{
    /// <summary>
    /// NuGet packages, stored in the <see cref="Package"/> tables.
    /// </summary>
    NuGet = 0,

    /// <summary>
    /// Dart and Flutter packages (the pub protocol), stored in the <see cref="Artifact"/> tables.
    /// </summary>
    Pub = 1,
}
