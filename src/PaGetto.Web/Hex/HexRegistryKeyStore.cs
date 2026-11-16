using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Storage;

namespace PaGetto.Web.Hex;

/// <summary>
/// The RSA keys that sign the registries of Hex feeds, one per feed. A feed's key is created on
/// first use and kept in the package storage, encrypted with Data Protection, whose key ring is
/// kept there too. Clients are given the public key, so a key must never change: if it can no
/// longer be decrypted, signing fails rather than a new key being made.
/// </summary>
public sealed class HexRegistryKeyStore
{
    private const string Purpose = "PaGetto.Hex.RegistryKey";
    private const int KeySizeInBits = 2048;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;

    // The private key of each feed as PEM, by feed slug. An RSA object is made for each use,
    // because instances are not safe to share between threads.
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _keys = new(StringComparer.Ordinal);

    public HexRegistryKeyStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider dataProtection)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _protector = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector(Purpose);
    }

    /// <summary>
    /// The feed's private key. The caller disposes it.
    /// </summary>
    public async Task<RSA> GetPrivateKeyAsync(Feed feed, CancellationToken cancellationToken)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(await GetPemAsync(feed, cancellationToken));
        return rsa;
    }

    /// <summary>
    /// The feed's public key, as a <c>PUBLIC KEY</c> PEM, which clients verify the registry with.
    /// </summary>
    public async Task<string> GetPublicKeyPemAsync(Feed feed, CancellationToken cancellationToken)
    {
        using var rsa = await GetPrivateKeyAsync(feed, cancellationToken);
        return rsa.ExportSubjectPublicKeyInfoPem() + "\n";
    }

    /// <summary>
    /// The SHA-256 fingerprint of a public key as Hex shows and checks it
    /// (<c>mix hex.repo add --fetch-public-key</c>): that of its SSH form, e.g. <c>SHA256:abc…</c>.
    /// </summary>
    public static string GetFingerprint(string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        using var blob = new MemoryStream();
        WriteSshString(blob, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteSshInteger(blob, parameters.Exponent);
        WriteSshInteger(blob, parameters.Modulus);

        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob.ToArray())).TrimEnd('=');
    }

    private Task<string> GetPemAsync(Feed feed, CancellationToken cancellationToken)
    {
        var key = _keys.GetOrAdd(feed.Slug, slug => new Lazy<Task<string>>(() => LoadOrCreateAsync(slug)));

        return WaitAsync(feed.Slug, key, cancellationToken);
    }

    private async Task<string> WaitAsync(string slug, Lazy<Task<string>> key, CancellationToken cancellationToken)
    {
        try
        {
            return await key.Value.WaitAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A failed load is tried again by the next request.
            _keys.TryRemove(KeyValuePair.Create(slug, key));
            throw;
        }
    }

    private async Task<string> LoadOrCreateAsync(string slug)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        var path = $"hex/{slug}/registry-key";

        var stored = await TryReadAsync(storage, path);
        if (stored == null)
        {
            using var rsa = RSA.Create(KeySizeInBits);
            var created = _protector.Protect(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));

            // Storage never overwrites, so of two servers creating a key at once, one key is kept
            // and both read it back.
            using (var content = new MemoryStream(created))
            {
                await storage.PutAsync(path, content, "application/octet-stream", CancellationToken.None);
            }

            stored = await TryReadAsync(storage, path)
                ?? throw new InvalidOperationException($"The registry key of Hex feed {slug} could not be stored at {path}.");
        }

        return Encoding.UTF8.GetString(_protector.Unprotect(stored));
    }

    private static async Task<byte[]> TryReadAsync(IStorageService storage, string path)
    {
        try
        {
            await using var stream = await storage.GetAsync(path, CancellationToken.None);
            if (stream == null)
            {
                return null;
            }

            using var content = new MemoryStream();
            await stream.CopyToAsync(content);
            return content.ToArray();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Storage providers throw their own errors for a missing path rather than returning
            // null. A key created after a read that failed for another reason is not stored over
            // the existing one (see above), so treating every error as a missing key is safe.
            return null;
        }
    }

    private static void WriteSshString(Stream output, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        output.Write(length);
        output.Write(value);
    }

    /// <summary>
    /// Writes an SSH <c>mpint</c>: a positive number with a leading zero byte when its high bit is set.
    /// </summary>
    private static void WriteSshInteger(Stream output, byte[] value)
    {
        var start = 0;
        while (start < value.Length - 1 && value[start] == 0)
        {
            start++;
        }

        var trimmed = value.AsSpan(start);
        if ((trimmed[0] & 0x80) != 0)
        {
            var padded = new byte[trimmed.Length + 1];
            trimmed.CopyTo(padded.AsSpan(1));
            WriteSshString(output, padded);
        }
        else
        {
            WriteSshString(output, trimmed);
        }
    }
}
