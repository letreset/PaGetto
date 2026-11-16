using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds Hex package tarballs for tests, the way <c>mix hex.build</c> writes them.
/// </summary>
public static class HexTarballBuilder
{
    /// <summary>
    /// A package with a module and a readme, whose metadata has the usual fields.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version.</param>
    /// <param name="readme">The README.md, or null for none.</param>
    /// <param name="requirements">The requirements as Erlang terms, e.g. <see cref="Requirement"/>s in a list.</param>
    public static byte[] Create(string name, string version, string readme = "# Readme", string requirements = "[]")
    {
        var files = new Dictionary<string, string>
        {
            ["lib/" + name + ".ex"] = "defmodule Demo do\nend\n",
            ["mix.exs"] = "defmodule Demo.MixProject do\nend\n",
        };

        if (readme != null)
        {
            files["README.md"] = readme;
        }

        return Create(Metadata(name, version, requirements), files);
    }

    /// <summary>
    /// A tarball with the given metadata and files.
    /// </summary>
    /// <param name="metadata">The content of <c>metadata.config</c>.</param>
    /// <param name="files">The files of <c>contents.tar.gz</c>.</param>
    /// <param name="checksum">The <c>CHECKSUM</c>, or null for the right one.</param>
    /// <param name="formatVersion">The <c>VERSION</c>.</param>
    public static byte[] Create(string metadata, IReadOnlyDictionary<string, string> files, string checksum = null, string formatVersion = "3")
    {
        var versionBytes = Encoding.ASCII.GetBytes(formatVersion);
        var metadataBytes = Encoding.UTF8.GetBytes(metadata);
        var contents = PubArchiveBuilder.Create(files).ToArray();

        if (checksum == null)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(versionBytes);
            hash.AppendData(metadataBytes);
            hash.AppendData(contents);
            checksum = Convert.ToHexString(hash.GetHashAndReset());
        }

        using var output = new MemoryStream();
        using (var tar = new TarWriter(output, TarEntryFormat.Ustar, leaveOpen: true))
        {
            Add(tar, "VERSION", versionBytes);
            Add(tar, "CHECKSUM", Encoding.ASCII.GetBytes(checksum));
            Add(tar, "metadata.config", metadataBytes);
            Add(tar, "contents.tar.gz", contents);
        }

        return output.ToArray();
    }

    /// <summary>
    /// The <c>metadata.config</c> of a package, as Erlang terms.
    /// </summary>
    public static string Metadata(string name, string version, string requirements = "[]")
    {
        return $$"""
            {<<"links">>,[{<<"GitHub">>,<<"https://github.com/example/{{name}}">>}]}.
            {<<"name">>,<<"{{name}}">>}.
            {<<"version">>,<<"{{version}}">>}.
            {<<"description">>,<<"A test package.">>}.
            {<<"elixir">>,<<"~> 1.15">>}.
            {<<"app">>,<<"{{name}}">>}.
            {<<"files">>,[<<"lib">>,<<"lib/{{name}}.ex">>,<<"mix.exs">>,<<"README.md">>]}.
            {<<"licenses">>,[<<"MIT">>]}.
            {<<"requirements">>,{{requirements}}}.
            {<<"build_tools">>,[<<"mix">>]}.

            """;
    }

    /// <summary>
    /// A requirement as <c>mix hex.build</c> writes it.
    /// </summary>
    public static string Requirement(string name, string requirement, string repository = "hexpm", bool optional = false)
    {
        return $"[{{<<\"name\">>,<<\"{name}\">>}},{{<<\"app\">>,<<\"{name}\">>}},{{<<\"optional\">>,{(optional ? "true" : "false")}}}," +
            $"{{<<\"requirement\">>,<<\"{requirement}\">>}},{{<<\"repository\">>,<<\"{repository}\">>}}]";
    }

    private static void Add(TarWriter tar, string name, byte[] content)
    {
        tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) });
    }
}
