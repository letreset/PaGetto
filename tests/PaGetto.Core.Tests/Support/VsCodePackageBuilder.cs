using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds VSIX files of VS Code extensions for tests, as <c>vsce package</c> writes them.
/// </summary>
public static class VsCodePackageBuilder
{
    /// <summary>
    /// The bytes of the icon that <see cref="Create"/> adds as <c>extension/icon.png</c>.
    /// </summary>
    public static readonly byte[] Icon = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 8, 9];

    /// <summary>
    /// The readme that <see cref="Create"/> adds as <c>extension/README.md</c>.
    /// </summary>
    public const string Readme = "# Hello World\n\nSays hello from the editor.\n";

    /// <summary>
    /// A VSIX file of a VS Code extension, with its package.json, readme, changelog and icon.
    /// </summary>
    /// <param name="publisher">The publisher.</param>
    /// <param name="name">The extension's name.</param>
    /// <param name="version">The version.</param>
    /// <param name="targetPlatform">The platform, or null for a universal file.</param>
    /// <param name="manifest">The whole <c>extension.vsixmanifest</c>, or null for <see cref="Manifest"/>.</param>
    /// <param name="packageJson">Whether the file has <c>extension/package.json</c>.</param>
    public static byte[] Create(
        string publisher,
        string name,
        string version,
        string targetPlatform = null,
        string manifest = null,
        bool packageJson = true)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["extension.vsixmanifest"] = Encoding.UTF8.GetBytes(manifest ?? Manifest(publisher, name, version, targetPlatform)),
            ["[Content_Types].xml"] = Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension=".json" ContentType="application/json"/><Default Extension=".md" ContentType="text/markdown"/><Default Extension=".png" ContentType="image/png"/><Default Extension=".js" ContentType="application/javascript"/><Default Extension=".vsixmanifest" ContentType="text/xml"/></Types>
                """),
            ["extension/README.md"] = Encoding.UTF8.GetBytes(Readme),
            ["extension/CHANGELOG.md"] = Encoding.UTF8.GetBytes("# Changelog\n"),
            ["extension/icon.png"] = Icon,
            ["extension/out/extension.js"] = Encoding.UTF8.GetBytes("exports.activate = () => {};\n"),
        };

        if (packageJson)
        {
            files["extension/package.json"] = Encoding.UTF8.GetBytes(
                $$"""{"name":"{{name}}","publisher":"{{publisher}}","version":"{{version}}","engines":{"vscode":"^1.90.0"},"main":"./out/extension.js"}""");
        }

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// An <c>extension.vsixmanifest</c> as <c>vsce package</c> writes it.
    /// </summary>
    /// <param name="publisher">The publisher.</param>
    /// <param name="name">The extension's name.</param>
    /// <param name="version">The version.</param>
    /// <param name="targetPlatform">The platform, or null for a universal file.</param>
    /// <param name="extraProperties">More elements of <c>Properties</c>, or null.</param>
    /// <param name="installationTarget">The installation target.</param>
    public static string Manifest(
        string publisher,
        string name,
        string version,
        string targetPlatform = null,
        string extraProperties = null,
        string installationTarget = "Microsoft.VisualStudio.Code")
    {
        var platform = targetPlatform == null ? "" : $" TargetPlatform=\"{targetPlatform}\"";

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
              <Metadata>
                <Identity Language="en-US" Id="{name}" Version="{version}" Publisher="{publisher}"{platform} />
                <DisplayName>Hello World</DisplayName>
                <Description xml:space="preserve">Says hello from the editor.</Description>
                <Tags>hello,greeting,__ext_hello</Tags>
                <Categories>Other,Snippets</Categories>
                <GalleryFlags>Public</GalleryFlags>
                <Properties>
                  <Property Id="Microsoft.VisualStudio.Code.Engine" Value="^1.90.0" />
                  <Property Id="Microsoft.VisualStudio.Code.ExtensionDependencies" Value="contoso.shared" />
                  <Property Id="Microsoft.VisualStudio.Code.ExtensionPack" Value="" />
                  <Property Id="Microsoft.VisualStudio.Services.Links.Source" Value="https://github.com/contoso/hello-world.git" />
                  {extraProperties}
                </Properties>
                <Icon>extension/icon.png</Icon>
              </Metadata>
              <Installation>
                <InstallationTarget Id="{installationTarget}" />
              </Installation>
              <Dependencies />
              <Assets>
                <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
                <Asset Type="Microsoft.VisualStudio.Services.Content.Details" Path="extension/README.md" Addressable="true" />
                <Asset Type="Microsoft.VisualStudio.Services.Content.Changelog" Path="extension/CHANGELOG.md" Addressable="true" />
                <Asset Type="Microsoft.VisualStudio.Services.Icons.Default" Path="extension/icon.png" Addressable="true" />
              </Assets>
            </PackageManifest>
            """;
    }
}
