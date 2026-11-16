using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Hex;

/// <summary>
/// Writes and reads the resources of a Hex repository (registry v2): <c>/names</c>,
/// <c>/versions</c> and <c>/packages/{name}</c>. Each is a protocol buffers message, wrapped in a
/// <c>Signed</c> message with its RSA SHA-512 signature, and gzipped.
/// See: https://github.com/hexpm/specifications/blob/main/registry-v2.md
/// </summary>
public static class HexRegistry
{
    /// <summary>
    /// The largest resource read from a mirror once decompressed, the same limit as Hex clients have.
    /// </summary>
    public const int MaxUncompressedBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Writes <c>/names</c>: the name of each package, with when its newest version was published.
    /// </summary>
    /// <param name="repository">The repository name, which clients check.</param>
    /// <param name="packages">The packages, by name, in order.</param>
    /// <param name="key">The repository's private key.</param>
    public static byte[] BuildNames(string repository, IEnumerable<KeyValuePair<string, IReadOnlyList<HexRelease>>> packages, RSA key)
    {
        var names = new ProtobufWriter();
        foreach (var (name, releases) in packages)
        {
            var package = new ProtobufWriter();
            package.WriteString(1, name);

            var updatedAt = releases.Max(r => r.PublishedAt);
            if (updatedAt != null)
            {
                package.WriteMessage(3, Timestamp(updatedAt.Value));
            }

            names.WriteMessage(1, package);
        }

        names.WriteString(2, repository);

        return Sign(names.ToArray(), key);
    }

    /// <summary>
    /// Writes <c>/versions</c>: the versions of each package, with the indexes of the retired ones.
    /// </summary>
    public static byte[] BuildVersions(string repository, IEnumerable<KeyValuePair<string, IReadOnlyList<HexRelease>>> packages, RSA key)
    {
        var versions = new ProtobufWriter();
        foreach (var (name, releases) in packages)
        {
            var package = new ProtobufWriter();
            package.WriteString(1, name);

            var retired = new List<int>();
            for (var i = 0; i < releases.Count; i++)
            {
                package.WriteString(2, releases[i].Version);
                if (releases[i].Retirement != null)
                {
                    retired.Add(i);
                }
            }

            package.WritePacked(3, retired);
            versions.WriteMessage(1, package);
        }

        versions.WriteString(2, repository);

        return Sign(versions.ToArray(), key);
    }

    /// <summary>
    /// Writes <c>/packages/{name}</c>: the versions of a package with their checksums, dependencies
    /// and retirement.
    /// </summary>
    public static byte[] BuildPackage(string repository, string name, IEnumerable<HexRelease> releases, RSA key)
    {
        var package = new ProtobufWriter();
        foreach (var release in releases)
        {
            var message = new ProtobufWriter();
            message.WriteString(1, release.Version);
            message.WriteBytes(2, release.InnerChecksum);

            foreach (var requirement in release.Requirements)
            {
                var dependency = new ProtobufWriter();
                dependency.WriteString(1, requirement.Name);
                dependency.WriteString(2, requirement.Requirement ?? string.Empty);
                if (requirement.Optional)
                {
                    dependency.WriteBool(3, true);
                }

                if (requirement.App != null && requirement.App != requirement.Name)
                {
                    dependency.WriteString(4, requirement.App);
                }

                if (requirement.Repository != null)
                {
                    dependency.WriteString(5, requirement.Repository);
                }

                message.WriteMessage(3, dependency);
            }

            if (release.Retirement != null)
            {
                var retired = new ProtobufWriter();
                retired.WriteVarint(1, (ulong)HexRetirement.GetReasonCode(release.Retirement.Reason));
                if (!string.IsNullOrEmpty(release.Retirement.Message))
                {
                    retired.WriteString(2, release.Retirement.Message);
                }

                message.WriteMessage(4, retired);
            }

            message.WriteBytes(5, release.OuterChecksum);

            if (release.PublishedAt != null)
            {
                message.WriteMessage(7, Timestamp(release.PublishedAt.Value));
            }

            package.WriteMessage(1, message);
        }

        package.WriteString(2, name);
        package.WriteString(3, repository);

        return Sign(package.ToArray(), key);
    }

    /// <summary>
    /// Reads the payload of a resource.
    /// </summary>
    /// <param name="resource">The gzipped resource.</param>
    /// <param name="publicKey">The repository's public key, or null to not check the signature.</param>
    /// <exception cref="InvalidDataException">The resource is not a gzipped signed message, or its signature is not valid.</exception>
    public static byte[] ReadPayload(byte[] resource, RSA publicKey = null)
    {
        try
        {
            using var input = new MemoryStream(resource);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var limited = new LimitedReadStream(
                gzip,
                MaxUncompressedBytes,
                () => new InvalidDataException($"The registry resource is larger than {MaxUncompressedBytes / 1024 / 1024} MiB."));
            using var signed = new MemoryStream();
            limited.CopyTo(signed);

            var reader = new ProtobufReader(signed.ToArray());
            byte[] payload = null, signature = null;
            while (reader.Next())
            {
                switch (reader.Field)
                {
                    case 1:
                        payload = reader.ReadBytes();
                        break;
                    case 2:
                        signature = reader.ReadBytes();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (payload == null)
            {
                throw new InvalidDataException("The registry resource has no payload.");
            }

            if (publicKey != null
                && (signature == null || !publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1)))
            {
                throw new InvalidDataException("The signature of the registry resource is not valid.");
            }

            return payload;
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("The registry resource is not a signed message.", e);
        }
    }

    /// <summary>
    /// Reads the versions of the payload of <c>/packages/{name}</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The payload is not a package, or not the package of this name.</exception>
    public static IReadOnlyList<HexRelease> ReadPackage(byte[] payload, string name)
    {
        try
        {
            var reader = new ProtobufReader(payload);
            var releases = new List<HexRelease>();
            string packageName = null;

            while (reader.Next())
            {
                switch (reader.Field)
                {
                    case 1:
                        releases.Add(ReadRelease(reader.ReadBytes()));
                        break;
                    case 2:
                        packageName = reader.ReadString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (packageName != name)
            {
                throw new InvalidDataException($"The registry lists package {packageName} instead of {name}.");
            }

            return releases;
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("The registry resource is not a package.", e);
        }
    }

    private static HexRelease ReadRelease(byte[] message)
    {
        var reader = new ProtobufReader(message);
        string version = null;
        byte[] innerChecksum = null;
        byte[] outerChecksum = null;
        HexRetirement retirement = null;
        DateTime? publishedAt = null;
        var requirements = new List<HexRequirement>();

        while (reader.Next())
        {
            switch (reader.Field)
            {
                case 1:
                    version = reader.ReadString();
                    break;
                case 2:
                    innerChecksum = reader.ReadBytes();
                    break;
                case 3:
                    var dependency = ReadDependency(reader.ReadBytes());
                    if (dependency.Name != null)
                    {
                        requirements.Add(dependency);
                    }

                    break;
                case 4:
                    retirement = ReadRetirement(reader.ReadBytes());
                    break;
                case 5:
                    outerChecksum = reader.ReadBytes();
                    break;
                case 7:
                    publishedAt = ReadTimestamp(reader.ReadBytes());
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new HexRelease
        {
            Version = version,
            InnerChecksum = innerChecksum,
            OuterChecksum = outerChecksum,
            Requirements = requirements,
            Retirement = retirement,
            PublishedAt = publishedAt,
        };
    }

    private static HexRequirement ReadDependency(byte[] message)
    {
        var reader = new ProtobufReader(message);
        string name = null, requirement = null, app = null, repository = null;
        var optional = false;

        while (reader.Next())
        {
            switch (reader.Field)
            {
                case 1:
                    name = reader.ReadString();
                    break;
                case 2:
                    requirement = reader.ReadString();
                    break;
                case 3:
                    optional = reader.ReadVarint() != 0;
                    break;
                case 4:
                    app = reader.ReadString();
                    break;
                case 5:
                    repository = reader.ReadString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new HexRequirement { Name = name, Requirement = requirement, Optional = optional, App = app, Repository = repository };
    }

    private static HexRetirement ReadRetirement(byte[] message)
    {
        var reader = new ProtobufReader(message);
        ulong reason = 0;
        string text = null;

        while (reader.Next())
        {
            switch (reader.Field)
            {
                case 1:
                    reason = reader.ReadVarint();
                    break;
                case 2:
                    text = reader.ReadString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new HexRetirement { Reason = HexRetirement.GetReason(reason), Message = text };
    }

    private static DateTime? ReadTimestamp(byte[] message)
    {
        var reader = new ProtobufReader(message);
        ulong seconds = 0;

        while (reader.Next())
        {
            if (reader.Field == 1)
            {
                seconds = reader.ReadVarint();
            }
            else
            {
                reader.Skip();
            }
        }

        return seconds is > 0 and < 253402300800UL ? DateTime.UnixEpoch.AddSeconds(seconds) : null;
    }

    private static ProtobufWriter Timestamp(DateTime time)
    {
        var timestamp = new ProtobufWriter();
        var ticks = (time.ToUniversalTime() - DateTime.UnixEpoch).Ticks;
        timestamp.WriteVarint(1, (ulong)(ticks / TimeSpan.TicksPerSecond));
        timestamp.WriteVarint(2, (ulong)(ticks % TimeSpan.TicksPerSecond * 100));
        return timestamp;
    }

    private static byte[] Sign(byte[] payload, RSA key)
    {
        var signed = new ProtobufWriter();
        signed.WriteBytes(1, payload);
        signed.WriteBytes(2, key.SignData(payload, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1));

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(signed.ToArray());
        }

        return output.ToArray();
    }
}
