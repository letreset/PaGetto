using System;
using System.Collections.Generic;

namespace PaGetto.Core.VsCode;

/// <summary>
/// An extension in the gallery API, as a query answers it, with its versions newest first.
/// </summary>
public sealed class VsCodeGalleryExtension
{
    public Guid ExtensionId { get; set; }

    /// <summary>
    /// The extension's name without the publisher, as published.
    /// </summary>
    public string ExtensionName { get; set; }

    public string DisplayName { get; set; }

    public string ShortDescription { get; set; }

    public VsCodeGalleryPublisher Publisher { get; set; }

    public List<VsCodeGalleryVersion> Versions { get; set; } = [];

    public List<VsCodeGalleryStatistic> Statistics { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public List<string> Categories { get; set; } = [];

    public DateTime ReleaseDate { get; set; }

    public DateTime PublishedDate { get; set; }

    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// The gallery flags, such as <c>Public Preview</c>; VS Code marks an extension whose flags
    /// include <c>preview</c> as a preview.
    /// </summary>
    public string Flags { get; set; }
}

/// <summary>
/// The publisher of an extension in the gallery API.
/// </summary>
public sealed class VsCodeGalleryPublisher
{
    public Guid PublisherId { get; set; }

    public string PublisherName { get; set; }

    public string DisplayName { get; set; }
}

/// <summary>
/// A statistic of an extension in the gallery API, such as <c>install</c>.
/// </summary>
public sealed class VsCodeGalleryStatistic
{
    public string StatisticName { get; set; }

    public double Value { get; set; }
}
