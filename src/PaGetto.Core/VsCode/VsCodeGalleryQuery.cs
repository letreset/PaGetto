using System.Collections.Generic;

namespace PaGetto.Core.VsCode;

/// <summary>
/// A query of the gallery API, the body VS Code posts to <c>extensionquery</c>. VS Code sends one
/// filter; the criteria of a filter are combined, except that several names or identifiers ask
/// for each of those extensions.
/// </summary>
public sealed class VsCodeGalleryQuery
{
    public List<VsCodeGalleryQueryFilter> Filters { get; set; } = [];

    public List<string> AssetTypes { get; set; } = [];

    /// <summary>
    /// What to include, as the flags of <see cref="VsCodeGalleryApi"/>.
    /// </summary>
    public int Flags { get; set; }
}

/// <summary>
/// A filter of a <see cref="VsCodeGalleryQuery"/>: its criteria and the page to return.
/// </summary>
public sealed class VsCodeGalleryQueryFilter
{
    public List<VsCodeGalleryCriterion> Criteria { get; set; } = [];

    /// <summary>
    /// The page to return, from 1.
    /// </summary>
    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public int SortBy { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// A criterion of a <see cref="VsCodeGalleryQueryFilter"/>, with a filter type of <see cref="VsCodeGalleryApi"/>.
/// </summary>
public sealed class VsCodeGalleryCriterion
{
    public int FilterType { get; set; }

    public string Value { get; set; }
}
