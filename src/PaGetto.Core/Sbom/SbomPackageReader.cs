using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Cargo;
using PaGetto.Core.CocoaPods;
using PaGetto.Core.Composer;
using PaGetto.Core.Cran;
using PaGetto.Core.Entities;
using PaGetto.Core.Go;
using PaGetto.Core.Hex;
using PaGetto.Core.Ivy;
using PaGetto.Core.Maven;
using PaGetto.Core.Npm;
using PaGetto.Core.Pub;
using PaGetto.Core.PyPI;
using PaGetto.Core.RubyGems;
using PaGetto.Core.Swift;
using PaGetto.Core.VsCode;
using PaGetto.Core.Vsix;

namespace PaGetto.Core.Sbom;

/// <summary>
/// Reads what an SBOM says about a stored version from the manifest stored with it: the name as
/// published, the license, the checksums and the dependencies the manifest declares. The
/// dependencies are the ones the package page shows; they are not resolved to versions.
/// </summary>
public static class SbomPackageReader
{
    /// <summary>
    /// Reads a NuGet package version. Its dependencies are listed once per target framework.
    /// </summary>
    public static SbomPackage Read(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var license = !string.IsNullOrEmpty(package.LicenseExpression) ? package.LicenseExpression : null;

        return new SbomPackage
        {
            FeedType = FeedType.NuGet,
            Name = package.Id,
            Version = package.Version.ToNormalizedString(),
            Description = package.Description,
            Licenses = license == null ? [] : [license],
            Dependencies = (package.Dependencies ?? [])
                .Where(d => !string.IsNullOrEmpty(d.Id))
                .Select(d => new SbomDependency(d.Id, Join(d.VersionRange, string.IsNullOrEmpty(d.TargetFramework) ? null : $"({d.TargetFramework})")))
                .ToList(),
        };
    }

    /// <summary>
    /// Reads a version of a feed of another type than NuGet.
    /// </summary>
    /// <param name="feedType">The type of the version's feed.</param>
    /// <param name="version">The version, with its <see cref="ArtifactVersion.Artifact"/>.</param>
    /// <param name="files">The files of the version, for a version made of several.</param>
    public static SbomPackage Read(FeedType feedType, ArtifactVersion version, IReadOnlyList<ArtifactFile> files)
    {
        ArgumentNullException.ThrowIfNull(version);

        var storedName = version.Artifact?.Name;
        var manifest = version.MetadataJson;
        string name = null;
        string license = null;
        IReadOnlyList<string> licenses = null;
        IEnumerable<KeyValuePair<string, string>> dependencies = null;

        switch (feedType)
        {
            case FeedType.Pub:
                dependencies = PubspecSummary.Parse(manifest).Dependencies;
                break;

            case FeedType.Npm:
                var npm = NpmManifestSummary.Parse(manifest);
                (license, dependencies) = (npm.License, npm.Dependencies);
                break;

            case FeedType.Maven:
                var pom = MavenPomSummary.Parse(manifest);
                (name, license, dependencies) = (pom.Coordinates, pom.License, pom.Dependencies);
                break;

            case FeedType.Ivy:
                var ivy = IvyManifestSummary.Parse(manifest);
                (name, license, dependencies) = (ivy.Name, ivy.License, ivy.Dependencies);
                break;

            case FeedType.PyPI:
                var pypi = PyPIManifestSummary.Parse(manifest);
                (name, license, dependencies) = (pypi.Name, pypi.License, pypi.Dependencies);
                files = files?.Where(f => !PyPIPackageService.IsMetadataFile(f.FileName)).ToList();
                break;

            case FeedType.Cargo:
                var cargo = CargoManifestSummary.Parse(manifest);
                (name, license, dependencies) = (cargo.Name, cargo.License, cargo.Dependencies);
                break;

            case FeedType.Go:
                var module = GoModuleManifest.Parse(manifest);
                name = module.Module ?? (GoModulePath.TryUnescape(storedName, out var modulePath) ? modulePath : null);
                dependencies = GoModFile.Parse(module.Mod)?.Requires
                    .Select(r => KeyValuePair.Create(r.Path, r.Indirect ? $"{r.Version} (indirect)" : r.Version));
                break;

            case FeedType.RubyGems:
                var gem = RubyGemsManifestSummary.Parse(manifest);
                (name, license, dependencies) = (gem.Name, gem.License, gem.Dependencies);
                break;

            case FeedType.Hex:
                var hex = HexManifest.Parse(manifest);
                (name, licenses) = (hex.Name, hex.Licenses);
                dependencies = hex.Requirements
                    .Select(r => KeyValuePair.Create(r.Name, r.Optional ? Join(r.Requirement, "(optional)") : r.Requirement));
                break;

            case FeedType.Composer:
                var composer = ComposerManifestSummary.Parse(manifest);
                (license, dependencies) = (composer.License, composer.Dependencies);
                break;

            case FeedType.CocoaPods:
                var pod = CocoaPodsManifestSummary.Parse(manifest);
                name = pod.Name ?? (CocoaPodsPodName.TryUnescape(storedName, out var podName) ? podName : null);
                (license, dependencies) = (pod.License, pod.Dependencies);
                break;

            case FeedType.Swift:
                name = SwiftManifestSummary.Parse(manifest).Id;
                break;

            case FeedType.Cran:
                var cran = CranManifestSummary.Parse(manifest);
                (name, license, dependencies) = (cran.Name, cran.License, cran.Dependencies);
                break;

            case FeedType.Vsix:
                var vsix = VsixManifest.Parse(manifest);
                name = vsix?.Id;
                dependencies = vsix?.Dependencies.Select(d => KeyValuePair.Create(d.Id, d.Version));
                break;

            case FeedType.VsCode:
                var vsCode = VsCodeVersionManifest.Parse(manifest)?.Main;
                name = vsCode?.Id;
                dependencies = GetVsCodeDependencies(vsCode);
                break;
        }

        return new SbomPackage
        {
            FeedType = feedType,
            Name = string.IsNullOrEmpty(name) ? storedName : name,
            Version = version.Version,
            Description = version.Description,
            Licenses = licenses ?? (string.IsNullOrWhiteSpace(license) ? [] : [license]),
            Sha256 = version.ContentHash,
            Files = files ?? [],
            Dependencies = (dependencies ?? [])
                .Where(d => !string.IsNullOrEmpty(d.Key))
                .Select(d => new SbomDependency(d.Key, string.IsNullOrWhiteSpace(d.Value) ? null : d.Value))
                .ToList(),
        };
    }

    // The extensions a VS Code extension depends on, and those of its extension pack.
    private static IEnumerable<KeyValuePair<string, string>> GetVsCodeDependencies(VsCodeManifest manifest)
    {
        if (manifest == null)
        {
            return null;
        }

        return $"{manifest.GetProperty(VsCodeGalleryApi.ExtensionDependenciesProperty)},{manifest.GetProperty(VsCodeGalleryApi.ExtensionPackProperty)}"
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => KeyValuePair.Create(id, (string)null));
    }

    private static string Join(string first, string second)
    {
        return string.Join(' ', new[] { first, second }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
