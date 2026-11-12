using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Npm;

/// <summary>
/// The hashes of a tarball: the SHA-512 <c>integrity</c> and SHA-1 <c>shasum</c> that npm clients
/// check, and the SHA-256 stored as the artifact's content hash.
/// </summary>
public sealed class NpmTarballHashes
{
    private const string Sha512Prefix = "sha512-";
    private const string Sha1Prefix = "sha1-";

    private readonly byte[] _sha512;
    private readonly byte[] _sha1;

    private NpmTarballHashes(byte[] sha512, byte[] sha1, byte[] sha256)
    {
        _sha512 = sha512;
        _sha1 = sha1;
        Sha256 = Convert.ToHexStringLower(sha256);
    }

    /// <summary>
    /// The subresource integrity string, e.g. <c>sha512-...</c>.
    /// </summary>
    public string Integrity => Sha512Prefix + Convert.ToBase64String(_sha512);

    /// <summary>
    /// The lowercase hex SHA-1.
    /// </summary>
    public string Shasum => Convert.ToHexStringLower(_sha1);

    /// <summary>
    /// The lowercase hex SHA-256.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Hashes a stream from its current position to its end.
    /// </summary>
    public static async Task<NpmTarballHashes> ComputeAsync(Stream tarball, CancellationToken cancellationToken)
    {
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
#pragma warning disable CA5350 // npm clients check the SHA-1 shasum of older packages; it is not used for security here.
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
#pragma warning restore CA5350

        var buffer = new byte[81920];
        int read;
        while ((read = await tarball.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sha512.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
        }

        return new NpmTarballHashes(sha512.GetHashAndReset(), sha1.GetHashAndReset(), sha256.GetHashAndReset());
    }

    /// <summary>
    /// Whether the tarball matches what a manifest's <c>dist</c> says about it. The strongest hash
    /// given is checked: SHA-512 integrity, then SHA-1 integrity, then the shasum. A manifest
    /// without any of them matches.
    /// </summary>
    /// <param name="integrity">The <c>dist.integrity</c>: one or more hashes separated by spaces.</param>
    /// <param name="shasum">The <c>dist.shasum</c>.</param>
    public bool Matches(string integrity, string shasum)
    {
        var sha512 = CompareIntegrity(integrity, Sha512Prefix, _sha512);
        if (sha512.HasValue) return sha512.Value;

        var sha1 = CompareIntegrity(integrity, Sha1Prefix, _sha1);
        if (sha1.HasValue) return sha1.Value;

        return string.IsNullOrEmpty(shasum) || string.Equals(shasum, Shasum, StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>Null if the integrity has no hash of the algorithm, otherwise whether one of them matches.</returns>
    private static bool? CompareIntegrity(string integrity, string prefix, byte[] actual)
    {
        bool? result = null;
        var expected = Convert.ToBase64String(actual);

        foreach (var token in (integrity ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Options may follow the hash after a "?".
            var hash = token[prefix.Length..].Split('?')[0];
            result = result == true || string.Equals(hash, expected, StringComparison.Ordinal);
        }

        return result;
    }
}
