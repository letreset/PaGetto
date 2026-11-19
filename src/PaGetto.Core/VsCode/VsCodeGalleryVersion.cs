using System;
using System.Collections.Generic;

namespace PaGetto.Core.VsCode;

/// <summary>
/// A version of an extension in the gallery API, for one platform: a version built for several
/// platforms is listed once per platform. VS Code reads a file of the version at
/// <c>{AssetUri}/{assetType}?targetPlatform={TargetPlatform}</c> when <see cref="Files"/> lists
/// that asset type, and downloads the VSIX file from <see cref="FallbackAssetUri"/>.
/// </summary>
public sealed class VsCodeGalleryVersion
{
    public string Version { get; set; }

    /// <summary>
    /// The platform, or <see cref="VsCodeTargetPlatform.Universal"/>. Mirrors may leave it out for universal files.
    /// </summary>
    public string TargetPlatform { get; set; }

    public DateTime LastUpdated { get; set; }

    public string AssetUri { get; set; }

    public string FallbackAssetUri { get; set; }

    public List<VsCodeGalleryFile> Files { get; set; } = [];

    public List<VsCodeGalleryProperty> Properties { get; set; } = [];
}

/// <summary>
/// A file of a version in the gallery API: its asset type and the URL it is at.
/// </summary>
public sealed class VsCodeGalleryFile
{
    public string AssetType { get; set; }

    public string Source { get; set; }
}
