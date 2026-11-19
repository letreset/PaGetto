using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using NuGet.Versioning;
using PaGetto.Core.CocoaPods;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Sbom;
using PaGetto.Core.Swift;

namespace PaGetto.Core.Tests.Sbom;

/// <summary>
/// A stored version of each feed type, as its manifest is stored, with what its SBOM should say.
/// </summary>
public static class SbomSamples
{
    public const string Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    public static IReadOnlyList<Sample> All { get; } =
    [
        Artifact(FeedType.Pub, "my_package", "1.0.0", """{"name": "my_package", "dependencies": {"http": "^1.0.0", "flutter": {"sdk": "flutter"}}}""",
            "my_package", null, ("http", "^1.0.0"), ("flutter", "SDK flutter")),
        Artifact(FeedType.Npm, "@scope/thing", "1.0.0", """{"license": "MIT", "dependencies": {"left-pad": "^1.3.0", "@scope/dep": "2.x"}}""",
            "@scope/thing", "MIT", ("left-pad", "^1.3.0"), ("@scope/dep", "2.x")),
        Artifact(FeedType.Maven, "com.example:my-lib", "1.0",
            """
            {"groupId": "com.example", "artifactId": "my-lib", "licenses": ["Apache License, Version 2.0"],
             "dependencies": [{"groupId": "org.slf4j", "artifactId": "slf4j-api", "version": "2.0.9"}, {"groupId": "org.junit", "artifactId": "junit", "scope": "test"}]}
            """,
            "com.example:my-lib", "Apache License, Version 2.0", ("org.slf4j:slf4j-api", "2.0.9"), ("org.junit:junit", "managed · test"))
            .WithFiles(File("my-lib-1.0.pom"), File("my-lib-1.0.jar")),
        Artifact(FeedType.Ivy, "com.example:tool", "1.0.0",
            """{"organisation": "com.Example", "module": "tool", "licenses": ["Apache-2.0"], "dependencies": [{"org": "org.scala-lang", "name": "scala-library", "rev": "2.12.20"}]}""",
            "com.Example:tool", "Apache-2.0", ("org.scala-lang:scala-library", "2.12.20"))
            .WithFiles(File("ivy-1.0.0.xml"), File("tool-1.0.0.jar")),
        Artifact(FeedType.PyPI, "my-package", "1.0",
            """{"name": "My_Package", "requires_dist": ["requests[socks]>=2.0", "rich"], "license_expression": "MIT OR Apache-2.0"}""",
            "My_Package", "MIT OR Apache-2.0", ("requests", "[socks]>=2.0"), ("rich", "any version"))
            .WithFiles(File("my_package-1.0-py3-none-any.whl"), File("my_package-1.0-py3-none-any.whl.metadata")),
        Artifact(FeedType.Cargo, "my_crate", "1.0.0",
            """{"name": "My_Crate", "license": "MIT OR Apache-2.0", "deps": [{"name": "serde", "req": "^1.0", "kind": "normal"}, {"name": "criterion", "req": "^0.5", "kind": "dev"}]}""",
            "My_Crate", "MIT OR Apache-2.0", ("serde", "^1.0")),
        Artifact(FeedType.Go, "github.com/!acme/lib", "v1.2.0",
            """{"module": "github.com/Acme/lib", "mod": "module github.com/Acme/lib\n\ngo 1.21\n\nrequire (\n\tgolang.org/x/text v0.14.0\n\tgithub.com/pkg/errors v0.9.1 // indirect\n)\n"}""",
            "github.com/Acme/lib", null, ("golang.org/x/text", "v0.14.0"), ("github.com/pkg/errors", "v0.9.1 (indirect)")),
        Artifact(FeedType.RubyGems, "rails", "7.1.0",
            """
            {"gems": [{"name": "Rails", "version": "7.1.0", "licenses": ["MIT"],
              "dependencies": [{"name": "rack", "requirement": [">= 2.2"], "type": "runtime"}, {"name": "rspec", "requirement": ["~> 3"], "type": "development"}]}]}
            """,
            "Rails", "MIT", ("rack", ">= 2.2"))
            .WithFiles(File("rails-7.1.0.gem")),
        Artifact(FeedType.Hex, "plug", "1.15.0",
            """
            {"name": "plug", "licenses": ["Apache-2.0"],
             "requirements": [{"name": "mime", "requirement": "~> 1.0", "optional": false}, {"name": "jason", "requirement": "~> 1.0", "optional": true}]}
            """,
            "plug", "Apache-2.0", ("mime", "~> 1.0"), ("jason", "~> 1.0 (optional)")),
        Artifact(FeedType.Composer, "acme/hello", "1.0.0", """{"license": ["MIT"], "require": {"php": ">=8.1", "psr/log": "^3.0"}}""",
            "acme/hello", "MIT", ("psr/log", "^3.0")),
        Artifact(FeedType.CocoaPods, CocoaPodsPodName.Escape("MyLib"), "1.0.0",
            """{"name": "MyLib", "license": {"type": "MIT", "file": "LICENSE"}, "dependencies": {"Alamofire": ["~> 5.0"], "Firebase/Core": []}}""",
            "MyLib", "MIT", ("Alamofire", "~> 5.0"), ("Firebase/Core", null)),
        Artifact(FeedType.Swift, "mona.linkedlist", "1.1.1",
            SwiftManifest.Create("mona.LinkedList", "1.1.1", new JsonObject(), null, null, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Dictionary<string, string>()).ToJson(),
            "mona.LinkedList", null),
        Artifact(FeedType.Cran, "hello", "1.0",
            CranManifest.Create(
                [
                    KeyValuePair.Create("Package", "Hello"),
                    KeyValuePair.Create("Version", "1.0"),
                    KeyValuePair.Create("License", "MIT + file LICENSE"),
                    KeyValuePair.Create("Imports", "rlang (>= 1.0.0), cli"),
                ],
                "0123"),
            "Hello", "MIT + file LICENSE", ("rlang", ">= 1.0.0"), ("cli", null)),
        Artifact(FeedType.Vsix, "acme.hello", "1.0.0",
            """{"id": "Acme.Hello", "version": "1.0.0", "dependencies": [{"id": "Acme.Base", "version": "[1.0,)"}]}""",
            "Acme.Hello", null, ("Acme.Base", "[1.0,)")),
        Artifact(FeedType.VsCode, "acme.hello", "1.0.0",
            """
            {"files": [{"publisher": "acme", "name": "hello", "version": "1.0.0", "targetPlatform": "universal",
              "properties": [{"key": "Microsoft.VisualStudio.Code.ExtensionDependencies", "value": "ms-python.python"}]}]}
            """,
            "acme.hello", null, ("ms-python.python", null))
            .WithFiles(File("acme.hello-1.0.0.vsix")),
        new Sample
        {
            FeedType = FeedType.NuGet,
            Package = new Package
            {
                Id = "Contoso.Lib",
                Version = new NuGetVersion("1.0.0"),
                Description = "A library.",
                LicenseExpression = "MIT",
                Dependencies =
                [
                    new PackageDependency { Id = "Newtonsoft.Json", VersionRange = "[13.0.1, )", TargetFramework = "net8.0" },
                    new PackageDependency { Id = "Newtonsoft.Json", VersionRange = "[12.0.1, )", TargetFramework = "netstandard2.0" },
                    new PackageDependency { TargetFramework = "net6.0" },
                ],
            },
            ExpectedName = "Contoso.Lib",
            ExpectedLicense = "MIT",
            ExpectedDependencies =
            [
                new SbomDependency("Newtonsoft.Json", "[13.0.1, ) (net8.0)"),
                new SbomDependency("Newtonsoft.Json", "[12.0.1, ) (netstandard2.0)"),
            ],
        },
    ];

    public static Sample Get(FeedType feedType)
    {
        return All.Single(s => s.FeedType == feedType);
    }

    /// <summary>
    /// The SBOM model of a sample, as the reader reads it.
    /// </summary>
    public static SbomPackage Read(Sample sample)
    {
        return sample.Package != null
            ? SbomPackageReader.Read(sample.Package)
            : SbomPackageReader.Read(sample.FeedType, sample.Version, sample.Files);
    }

    private static Sample Artifact(
        FeedType feedType,
        string storedName,
        string version,
        string manifest,
        string expectedName,
        string expectedLicense,
        params (string Name, string Requirement)[] expectedDependencies)
    {
        return new Sample
        {
            FeedType = feedType,
            Version = new ArtifactVersion
            {
                Artifact = new Entities.Artifact { Name = storedName },
                Version = version,
                Description = $"The {feedType} sample.",
                MetadataJson = manifest,
                ContentHash = Sha256,
            },
            Sha256 = Sha256,
            ExpectedName = expectedName,
            ExpectedLicense = expectedLicense,
            ExpectedDependencies = expectedDependencies.Select(d => new SbomDependency(d.Name, d.Requirement)).ToList(),
        };
    }

    private static ArtifactFile File(string fileName)
    {
        return new ArtifactFile
        {
            FileName = fileName,
            Md5 = "098f6bcd4621d373cade4e832627b4f6",
            Sha1 = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3",
            Sha256 = Sha256,
            Sha512 = "ee26b0dd4af7e749aa1a8ee3c10ae9923f618980772e473f8819a5d4940e0db27ac185f8a0e1d5f84f88bc887fd67b143732c304cc5fa9ad8e6f57f50028a8ff",
        };
    }

    public sealed record Sample
    {
        public FeedType FeedType { get; init; }

        /// <summary>
        /// The stored version of a feed of another type than NuGet.
        /// </summary>
        public ArtifactVersion Version { get; init; }

        /// <summary>
        /// The stored NuGet package version.
        /// </summary>
        public Package Package { get; init; }

        public IReadOnlyList<ArtifactFile> Files { get; init; } = [];

        /// <summary>
        /// The SHA-256 the version's archive has, or null when it is made of files.
        /// </summary>
        public string Sha256 { get; init; }

        public string ExpectedName { get; init; }
        public string ExpectedLicense { get; init; }
        public IReadOnlyList<SbomDependency> ExpectedDependencies { get; init; } = [];

        /// <summary>
        /// The sample as a version made of files, which has no archive of its own.
        /// </summary>
        public Sample WithFiles(params ArtifactFile[] files)
        {
            Version.ContentHash = null;
            return this with { Files = files, Sha256 = null };
        }

        public override string ToString()
        {
            return FeedType.ToString();
        }
    }
}
