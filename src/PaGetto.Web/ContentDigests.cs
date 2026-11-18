using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace PaGetto.Web;

/// <summary>
/// The digests of response bodies that are served many times from one array, such as the index
/// files kept by <see cref="Core.Artifacts.ArtifactIndexCache"/>: they are computed once for each
/// array and forgotten with it.
/// </summary>
public static class ContentDigests
{
    private static readonly ConditionalWeakTable<byte[], Digests> _digests = new();

    /// <summary>
    /// The MD5 of the content, as lowercase hex.
    /// </summary>
    public static string GetMd5Hex(byte[] content)
    {
        return Get(content).Md5Hex;
    }

    /// <summary>
    /// The SHA-256 of the content.
    /// </summary>
    public static byte[] GetSha256(byte[] content)
    {
        return Get(content).Sha256;
    }

    private static Digests Get(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return _digests.GetValue(content, c => new Digests
        {
#pragma warning disable CA5351 // MD5 is what clients such as Bundler check files with, not for security.
            Md5Hex = Convert.ToHexStringLower(MD5.HashData(c)),
#pragma warning restore CA5351
            Sha256 = SHA256.HashData(c),
        });
    }

    private sealed class Digests
    {
        public string Md5Hex { get; init; }

        public byte[] Sha256 { get; init; }
    }
}
