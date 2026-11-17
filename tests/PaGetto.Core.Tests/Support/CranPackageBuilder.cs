using System.Collections.Generic;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds R source packages for tests.
/// </summary>
public static class CranPackageBuilder
{
    /// <summary>
    /// A source package with its files in a folder named after the package, as <c>R CMD build</c> writes it.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version.</param>
    /// <param name="readme">The content of <c>README.md</c>, or null for none.</param>
    /// <param name="description">The whole <c>DESCRIPTION</c> file, or null for <see cref="Description"/>.</param>
    /// <param name="folder">The folder of the files, or null for the package name.</param>
    public static byte[] Create(string name, string version, string readme = null, string description = null, string folder = null)
    {
        folder = (folder ?? name) + "/";
        var files = new Dictionary<string, string>
        {
            [folder + "DESCRIPTION"] = description ?? Description(name, version),
            [folder + "NAMESPACE"] = "export(hello)\n",
            [folder + "R/hello.R"] = "hello <- function() \"Hello\"\n",
        };

        if (readme != null)
        {
            files[folder + "README.md"] = readme;
        }

        return PubArchiveBuilder.Create(files).ToArray();
    }

    /// <summary>
    /// A <c>DESCRIPTION</c> file as <c>R CMD build</c> writes it, with optional extra fields.
    /// </summary>
    public static string Description(string name, string version, string extraFields = null)
    {
        return $"""
            Package: {name}
            Type: Package
            Title: A Test Package
            Version: {version}
            Authors@R: person("Ada", "Lovelace", email = "ada@example.com", role = c("aut", "cre"))
            Description: A package for tests. It says hello
                and does nothing else.
            License: MIT + file LICENSE
            Depends: R (>= 4.1.0)
            Imports: rlang (>= 1.0.0), cli
            Encoding: UTF-8
            NeedsCompilation: no
            Packaged: 2020-01-01 00:00:00 UTC; ada
            Author: Ada Lovelace [aut, cre]
            Maintainer: Ada Lovelace <ada@example.com>
            {extraFields}
            """.TrimEnd() + "\n";
    }
}
