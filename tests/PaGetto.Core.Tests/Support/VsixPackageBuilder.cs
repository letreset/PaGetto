using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds VSIX files of Visual Studio extensions for tests.
/// </summary>
public static class VsixPackageBuilder
{
    /// <summary>
    /// The bytes of the icon that <see cref="Create"/> adds as <c>Resources/Icon.png</c>.
    /// </summary>
    public static readonly byte[] Icon = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    /// <summary>
    /// The bytes of the preview image that <see cref="Create"/> adds as <c>Resources/Preview.png</c>.
    /// </summary>
    public static readonly byte[] PreviewImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 4, 5, 6];

    /// <summary>
    /// A VSIX file as the VSIX project of the Visual Studio SDK writes it, with an icon and a preview image.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="version">The version.</param>
    /// <param name="manifest">The whole <c>extension.vsixmanifest</c>, or null for <see cref="Manifest"/>.</param>
    /// <param name="contentTypes">Whether the file has <c>[Content_Types].xml</c>.</param>
    /// <param name="extraFiles">More files, or null.</param>
    public static byte[] Create(
        string id,
        string version,
        string manifest = null,
        bool contentTypes = true,
        IReadOnlyDictionary<string, byte[]> extraFiles = null)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["extension.vsixmanifest"] = Encoding.UTF8.GetBytes(manifest ?? Manifest(id, version)),
            ["HelloTools.dll"] = [0x4D, 0x5A, 0, 0],
            ["HelloTools.pkgdef"] = Encoding.UTF8.GetBytes("[$RootKey$\\Packages]\n"),
            ["Resources/Icon.png"] = Icon,
            ["Resources/Preview.png"] = PreviewImage,
            ["catalog.json"] = Encoding.UTF8.GetBytes("{}"),
            ["manifest.json"] = Encoding.UTF8.GetBytes("{}"),
        };

        if (contentTypes)
        {
            files["[Content_Types].xml"] = Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="vsixmanifest" ContentType="text/xml" />
                  <Default Extension="dll" ContentType="application/octet-stream" />
                  <Default Extension="pkgdef" ContentType="text/plain" />
                  <Default Extension="png" ContentType="application/octet-stream" />
                  <Default Extension="json" ContentType="application/json" />
                </Types>
                """);
        }

        foreach (var (name, content) in extraFiles ?? new Dictionary<string, byte[]>())
        {
            files[name] = content;
        }

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// An <c>extension.vsixmanifest</c> as the VSIX project writes it, for Visual Studio 2022 on x64 and Arm64.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="version">The version.</param>
    /// <param name="extraMetadata">More elements of <c>Metadata</c>, or null.</param>
    /// <param name="installation">The elements of <c>Installation</c>, or null for Visual Studio 2022.</param>
    public static string Manifest(string id, string version, string extraMetadata = null, string installation = null)
    {
        installation ??= """
            <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0, 18.0)">
                  <ProductArchitecture>amd64</ProductArchitecture>
                </InstallationTarget>
                <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0, 18.0)">
                  <ProductArchitecture>arm64</ProductArchitecture>
                </InstallationTarget>
            """;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
              <Metadata>
                <Identity Id="{id}" Version="{version}" Language="en-US" Publisher="Contoso" />
                <DisplayName>Hello Tools</DisplayName>
                <Description xml:space="preserve">Says hello from the editor.</Description>
                <MoreInfo>https://github.com/contoso/hello-tools</MoreInfo>
                <ReleaseNotes>ReleaseNotes.txt</ReleaseNotes>
                <Icon>Resources\Icon.png</Icon>
                <PreviewImage>Resources\Preview.png</PreviewImage>
                <Tags>hello; greeting, editor</Tags>
                {extraMetadata}
              </Metadata>
              <Installation>
                {installation}
              </Installation>
              <Dependencies>
                <Dependency Id="Microsoft.Framework.NDP" DisplayName="Microsoft .NET Framework" d:Source="Manual" Version="[4.5,)" />
                <Dependency Id="Contoso.Shared" DisplayName="Contoso Shared" d:Source="Installed" Version="[1.0,)" />
              </Dependencies>
              <Prerequisites>
                <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor" Version="[17.0,18.0)" DisplayName="Visual Studio core editor" />
              </Prerequisites>
              <Assets>
                <Asset Type="Microsoft.VisualStudio.VsPackage" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
              </Assets>
            </PackageManifest>
            """;
    }
}
