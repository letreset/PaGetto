namespace PaGetto.Core.VsCode;

/// <summary>
/// A property of a version of a VS Code extension, such as the VS Code versions it needs
/// (<see cref="VsCodeGalleryApi.EngineProperty"/>), from the <c>Properties</c> of its manifest.
/// The gallery API lists them with the version.
/// </summary>
public sealed class VsCodeGalleryProperty
{
    public string Key { get; init; }

    public string Value { get; init; }
}
