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

    /// <summary>
    /// JavaScript and TypeScript packages (the npm registry protocol), stored in the <see cref="Artifact"/> tables.
    /// </summary>
    Npm = 2,

    /// <summary>
    /// Java, Kotlin and Scala libraries (the Maven repository layout), stored in the <see cref="Artifact"/>
    /// tables with the files of each version in <see cref="ArtifactFile"/>.
    /// </summary>
    Maven = 3,

    /// <summary>
    /// Python packages (the simple repository API and the legacy upload API of PyPI), stored in the
    /// <see cref="Artifact"/> tables with the wheels and sdists of each version in <see cref="ArtifactFile"/>.
    /// </summary>
    PyPI = 4,

    /// <summary>
    /// Rust crates (Cargo's sparse registry protocol and the registry web API), stored in the
    /// <see cref="Artifact"/> tables.
    /// </summary>
    Cargo = 5,

    /// <summary>
    /// Go modules (the GOPROXY protocol, with an upload endpoint of this server), stored in the
    /// <see cref="Artifact"/> tables.
    /// </summary>
    Go = 6,
}
