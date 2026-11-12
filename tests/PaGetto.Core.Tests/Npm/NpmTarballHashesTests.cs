using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Npm;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmTarballHashesTests
{
    private static readonly byte[] _content = [1, 2, 3, 4, 5];

#pragma warning disable CA5350 // The SHA-1 the npm client checks.
    private static readonly string _sha1Hex = Convert.ToHexStringLower(SHA1.HashData(_content));
    private static readonly string _sha1Integrity = "sha1-" + Convert.ToBase64String(SHA1.HashData(_content));
#pragma warning restore CA5350
    private static readonly string _sha512Integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(_content));

    [Fact]
    public async Task ComputesTheHashesNpmUses()
    {
        var hashes = await ComputeAsync();

        Assert.Equal(_sha512Integrity, hashes.Integrity);
        Assert.Equal(_sha1Hex, hashes.Shasum);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(_content)), hashes.Sha256);
    }

    [Fact]
    public async Task ChecksTheStrongestHashGiven()
    {
        var hashes = await ComputeAsync();
        const string wrongSha512 = "sha512-AAAA";

        Assert.True(hashes.Matches(_sha512Integrity, null));
        Assert.True(hashes.Matches($"{wrongSha512} {_sha512Integrity}", null));
        Assert.True(hashes.Matches(_sha1Integrity, null));
        Assert.True(hashes.Matches(null, _sha1Hex.ToUpperInvariant()));

        // A wrong SHA-512 is not made up for by a right SHA-1.
        Assert.False(hashes.Matches($"{wrongSha512} {_sha1Integrity}", _sha1Hex));
        Assert.False(hashes.Matches(null, "0000"));
    }

    [Fact]
    public async Task AcceptsAManifestWithoutHashes()
    {
        Assert.True((await ComputeAsync()).Matches(null, null));
        Assert.True((await ComputeAsync()).Matches("md5-abc", null));
    }

    private static Task<NpmTarballHashes> ComputeAsync()
    {
        return NpmTarballHashes.ComputeAsync(new MemoryStream(_content), CancellationToken.None);
    }
}
