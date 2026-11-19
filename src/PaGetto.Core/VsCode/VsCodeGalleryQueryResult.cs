using System.Collections.Generic;

namespace PaGetto.Core.VsCode;

/// <summary>
/// The answer to a <see cref="VsCodeGalleryQuery"/>: one result, with a page of extensions and
/// the number of extensions that match.
/// </summary>
public sealed class VsCodeGalleryQueryResult
{
    public List<VsCodeGalleryResult> Results { get; set; } = [];

    /// <summary>
    /// The answer with one page of extensions.
    /// </summary>
    public static VsCodeGalleryQueryResult Create(List<VsCodeGalleryExtension> extensions, int totalCount)
    {
        return new VsCodeGalleryQueryResult
        {
            Results =
            [
                new VsCodeGalleryResult
                {
                    Extensions = extensions,
                    ResultMetadata =
                    [
                        new VsCodeGalleryResultMetadata
                        {
                            MetadataType = "ResultCount",
                            MetadataItems = [new VsCodeGalleryMetadataItem { Name = "TotalCount", Count = totalCount }],
                        },
                    ],
                },
            ],
        };
    }
}

/// <summary>
/// A result of a <see cref="VsCodeGalleryQueryResult"/>.
/// </summary>
public sealed class VsCodeGalleryResult
{
    public List<VsCodeGalleryExtension> Extensions { get; set; } = [];

    public List<VsCodeGalleryResultMetadata> ResultMetadata { get; set; } = [];

    /// <summary>
    /// The number of extensions that match, or the number of extensions of the page when it is not given.
    /// </summary>
    public int GetTotalCount()
    {
        foreach (var metadata in ResultMetadata ?? [])
        {
            if (metadata.MetadataType == "ResultCount")
            {
                foreach (var item in metadata.MetadataItems ?? [])
                {
                    if (item.Name == "TotalCount")
                    {
                        return item.Count;
                    }
                }
            }
        }

        return Extensions?.Count ?? 0;
    }
}

/// <summary>
/// Metadata of a <see cref="VsCodeGalleryResult"/>, such as the result count.
/// </summary>
public sealed class VsCodeGalleryResultMetadata
{
    public string MetadataType { get; set; }

    public List<VsCodeGalleryMetadataItem> MetadataItems { get; set; } = [];
}

/// <summary>
/// An item of <see cref="VsCodeGalleryResultMetadata"/>.
/// </summary>
public sealed class VsCodeGalleryMetadataItem
{
    public string Name { get; set; }

    public int Count { get; set; }
}
