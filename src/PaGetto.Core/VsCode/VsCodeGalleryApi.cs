namespace PaGetto.Core.VsCode;

/// <summary>
/// The names and numbers of the gallery API that VS Code and the editors built on it query: the
/// API of the Visual Studio Marketplace, which Open VSX also serves. They are taken from VS Code's
/// <c>extensionGalleryService.ts</c> and <c>extensionGalleryManifestService.ts</c>.
/// </summary>
public static class VsCodeGalleryApi
{
    /// <summary>
    /// The installation target of VS Code extensions, in their manifest and in every query.
    /// </summary>
    public const string InstallationTarget = "Microsoft.VisualStudio.Code";

    /// <summary>
    /// The media type VS Code accepts for queries.
    /// </summary>
    public const string QueryAcceptHeader = "application/json;api-version=3.0-preview.1";

    // Asset types: the files of a version, at {assetUri}/{assetType}.
    public const string VsixPackageAsset = "Microsoft.VisualStudio.Services.VSIXPackage";
    public const string VsixManifestAsset = "Microsoft.VisualStudio.Services.VsixManifest";
    public const string ManifestAsset = "Microsoft.VisualStudio.Code.Manifest";
    public const string DetailsAsset = "Microsoft.VisualStudio.Services.Content.Details";
    public const string ChangelogAsset = "Microsoft.VisualStudio.Services.Content.Changelog";
    public const string LicenseAsset = "Microsoft.VisualStudio.Services.Content.License";
    public const string IconAsset = "Microsoft.VisualStudio.Services.Icons.Default";

    // Version properties.
    public const string PreReleaseProperty = "Microsoft.VisualStudio.Code.PreRelease";
    public const string EngineProperty = "Microsoft.VisualStudio.Code.Engine";
    public const string ExtensionDependenciesProperty = "Microsoft.VisualStudio.Code.ExtensionDependencies";
    public const string ExtensionPackProperty = "Microsoft.VisualStudio.Code.ExtensionPack";
    public const string SourceLinkProperty = "Microsoft.VisualStudio.Services.Links.Source";

    /// <summary>
    /// Marks an extension of a private marketplace. VS Code installs a private extension without a
    /// signature, but refuses an unsigned public one from a gallery that <c>product.json</c> sets.
    /// </summary>
    public const string PrivateProperty = "PrivateMarketplace";

    // Filter types of a query's criteria.
    public const int TagFilter = 1;
    public const int ExtensionIdFilter = 4;
    public const int CategoryFilter = 5;
    public const int ExtensionNameFilter = 7;
    public const int TargetFilter = 8;
    public const int SearchTextFilter = 10;

    // Flags of a query.
    public const int IncludeVersionsFlag = 0x1;
    public const int IncludeLatestVersionOnlyFlag = 0x200;
    public const int IncludeLatestPrereleaseAndStableVersionOnlyFlag = 0x10000;

    /// <summary>
    /// The flags this server sends to a mirror to read an extension: its versions with their
    /// files, properties and asset URIs, its categories, tags and statistics.
    /// </summary>
    public const int ReadExtensionFlags = 0x1 | 0x2 | 0x4 | 0x10 | 0x80 | 0x100;

    // Sort orders of a query.
    public const int SortByLastUpdated = 1;
    public const int SortByTitle = 2;
    public const int SortByPublisherName = 3;
    public const int SortByInstallCount = 4;
    public const int SortByPublishedDate = 10;
}
