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

    /// <summary>
    /// Ruby gems (the compact index of RubyGems.org and its push API), stored in the
    /// <see cref="Artifact"/> tables with the gem file of each platform of a version in <see cref="ArtifactFile"/>.
    /// </summary>
    RubyGems = 7,

    /// <summary>
    /// Elixir and Erlang packages (the Hex repository format and HTTP API), stored in the
    /// <see cref="Artifact"/> tables.
    /// </summary>
    Hex = 8,

    /// <summary>
    /// PHP packages (the Composer v2 repository format, with an upload endpoint of this server), stored
    /// in the <see cref="Artifact"/> tables.
    /// </summary>
    Composer = 9,

    /// <summary>
    /// Swift and Objective-C pods (a CocoaPods spec repository in the CDN format, with a publish endpoint
    /// of this server), stored in the <see cref="Artifact"/> tables.
    /// </summary>
    CocoaPods = 10,

    /// <summary>
    /// Swift packages (the Swift package registry API of Swift Package Manager), stored in the
    /// <see cref="Artifact"/> tables.
    /// </summary>
    Swift = 11,

    /// <summary>
    /// R packages (a CRAN-like repository of source packages, with an upload endpoint of this server),
    /// stored in the <see cref="Artifact"/> tables.
    /// </summary>
    Cran = 12,

    /// <summary>
    /// Java and Scala libraries and sbt plugins in the Ivy repository layout that sbt publishes with
    /// <c>Resolver.ivyStylePatterns</c>, stored in the <see cref="Artifact"/> tables.
    /// </summary>
    Ivy = 13,

    /// <summary>
    /// Visual Studio extensions (VSIX files), served as the Atom feed of a private gallery that
    /// Visual Studio reads, with an upload endpoint of this server, stored in the <see cref="Artifact"/> tables.
    /// </summary>
    Vsix = 14,

    /// <summary>
    /// VS Code extensions (VSIX files), served through the gallery API that VS Code and the editors
    /// built on it query, with an upload endpoint of this server, stored in the <see cref="Artifact"/>
    /// tables with the VSIX file of each platform of a version in <see cref="ArtifactFile"/>.
    /// </summary>
    VsCode = 15,
}
