using System;

namespace PaGetto.Core.PyPI;

/// <summary>
/// The name of a distribution file of a Python project: a wheel
/// (<c>{name}-{version}[-{build}]-{python}-{abi}-{platform}.whl</c>) or a source distribution
/// (<c>{name}-{version}.tar.gz</c>, or <c>.zip</c> for older ones). Eggs and other old formats are
/// not served.
/// See: https://packaging.python.org/en/latest/specifications/binary-distribution-format/
/// </summary>
public sealed class PyPIFileName
{
    /// <summary>
    /// The suffix of the file that holds a wheel's core metadata next to it (PEP 658).
    /// </summary>
    public const string MetadataSuffix = ".metadata";

    private const string WheelExtension = ".whl";

    // The file name column has 256 characters, and the metadata file of a wheel is stored next to
    // it under the wheel's name and the suffix.
    private static readonly int MaxLength = 256 - MetadataSuffix.Length;

    private static readonly string[] SdistExtensions = [".tar.gz", ".zip"];

    private PyPIFileName(string fileName, string version, bool isWheel, string extension)
    {
        FileName = fileName;
        Version = version;
        IsWheel = isWheel;
        Extension = extension;
    }

    public string FileName { get; }

    /// <summary>
    /// The version in the file name, normalized.
    /// </summary>
    public string Version { get; }

    public bool IsWheel { get; }

    /// <summary>
    /// The extension, e.g. <c>.whl</c> or <c>.tar.gz</c>.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// The <c>filetype</c> of the legacy upload API: <c>bdist_wheel</c> or <c>sdist</c>.
    /// </summary>
    public string PackageType => IsWheel ? "bdist_wheel" : "sdist";

    /// <summary>
    /// Reads the version from the name of a file of a project.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="projectName">The project the file belongs to; the name in the file must match it.</param>
    /// <param name="result">The parsed name.</param>
    /// <returns>False if the name is not that of a wheel or sdist of the project.</returns>
    public static bool TryParse(string fileName, string projectName, out PyPIFileName result)
    {
        result = null;
        if (string.IsNullOrEmpty(fileName)
            || fileName.Length > MaxLength
            || fileName.AsSpan().IndexOfAny("/\\:") >= 0
            || fileName.StartsWith('.')
            || string.IsNullOrEmpty(projectName))
        {
            return false;
        }

        foreach (var c in fileName)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        var project = PyPIPackageName.Normalize(projectName);

        if (fileName.EndsWith(WheelExtension, StringComparison.OrdinalIgnoreCase))
        {
            // The name and version of a wheel have their dashes replaced by underscores, so the
            // parts are separated by the only dashes there are.
            var parts = fileName[..^WheelExtension.Length].Split('-');
            if (parts.Length is not (5 or 6)
                || PyPIPackageName.Normalize(parts[0]) != project
                || !PyPIVersion.TryParse(parts[1], out var wheelVersion))
            {
                return false;
            }

            result = new PyPIFileName(fileName, wheelVersion.Value, isWheel: true, WheelExtension);
            return true;
        }

        foreach (var extension in SdistExtensions)
        {
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Older sdists keep the dashes of their name, so the version starts after whichever
            // dash ends the project's name.
            var stem = fileName[..^extension.Length];
            for (var dash = stem.IndexOf('-'); dash > 0; dash = stem.IndexOf('-', dash + 1))
            {
                if (PyPIPackageName.Normalize(stem[..dash]) == project
                    && PyPIVersion.TryParse(stem[(dash + 1)..], out var sdistVersion))
                {
                    result = new PyPIFileName(fileName, sdistVersion.Value, isWheel: false, extension);
                    return true;
                }
            }

            return false;
        }

        return false;
    }
}
