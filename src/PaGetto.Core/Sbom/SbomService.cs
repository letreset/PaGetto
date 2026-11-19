using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Vulnerabilities;

namespace PaGetto.Core.Sbom;

/// <summary>
/// Makes the software bill of materials of a stored version, in CycloneDX JSON, from its stored
/// manifest and its known vulnerabilities.
/// </summary>
public class SbomService
{
    private static readonly string _toolVersion = GetToolVersion();

    private readonly IPackageDatabase _packages;
    private readonly IArtifactDatabase _artifacts;
    private readonly IVulnerabilityService _vulnerabilities;
    private readonly SystemTime _time;

    public SbomService(IPackageDatabase packages, IArtifactDatabase artifacts, IVulnerabilityService vulnerabilities, SystemTime time)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _vulnerabilities = vulnerabilities ?? throw new ArgumentNullException(nameof(vulnerabilities));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The CycloneDX document of a version of a feed, unlisted versions included.
    /// </summary>
    /// <param name="feed">The feed of the version.</param>
    /// <param name="name">The package name, as published or as stored.</param>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The document as UTF-8 JSON, or null when the feed has no such version.</returns>
    public async Task<byte[]> GetCycloneDxOrNullAsync(Feed feed, string name, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
        {
            return null;
        }

        if (feed.Type == FeedType.NuGet)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return null;
            }

            var package = await _packages.FindOrNullAsync(feed.Id, name, nugetVersion, includeUnlisted: true, cancellationToken);
            if (package == null)
            {
                return null;
            }

            var packageVulnerabilities = await _vulnerabilities.GetForPackageAsync(package.Key, cancellationToken);
            return CycloneDxWriter.Write(SbomPackageReader.Read(package), packageVulnerabilities, Guid.NewGuid(), _time.UtcNow, _toolVersion);
        }

        var storedName = ArtifactNames.GetStoredName(feed.Type, name);
        var artifactVersion = await _artifacts.FindVersionOrNullAsync(feed.Id, storedName, version, cancellationToken);
        if (artifactVersion == null)
        {
            return null;
        }

        var files = await _artifacts.FindFilesAsync(feed.Id, storedName, artifactVersion.Version, cancellationToken);
        var vulnerabilities = await _vulnerabilities.GetForArtifactVersionAsync(artifactVersion.Key, cancellationToken);

        return CycloneDxWriter.Write(SbomPackageReader.Read(feed.Type, artifactVersion, files), vulnerabilities, Guid.NewGuid(), _time.UtcNow, _toolVersion);
    }

    // The server's version without the commit, e.g. 1.2.0 of 1.2.0+4712.
    private static string GetToolVersion()
    {
        var version = typeof(SbomService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;

        return plus < 0 ? version : version[..plus];
    }
}
