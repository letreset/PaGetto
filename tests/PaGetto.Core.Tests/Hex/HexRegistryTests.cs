using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class HexRegistryTests
{
    private static readonly RSA _key = RSA.Create(2048);

    private static readonly HexRelease _release = new()
    {
        Version = "1.0.0",
        InnerChecksum = Checksum(1),
        OuterChecksum = Checksum(2),
        Requirements =
        [
            new HexRequirement { Name = "jason", Requirement = "~> 1.4" },
            new HexRequirement { Name = "plug", Requirement = ">= 0.0.0", Optional = true, App = "plug_app", Repository = "hexpm" },
        ],
        PublishedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
    };

    private static readonly HexRelease _retired = new()
    {
        Version = "1.1.0-rc.1",
        InnerChecksum = Checksum(3),
        OuterChecksum = Checksum(4),
        Retirement = new HexRetirement { Reason = "security", Message = "Use 1.0.0" },
    };

    public class BuildPackage
    {
        [Fact]
        public void WritesASignedPackageThatReadsBack()
        {
            var resource = HexRegistry.BuildPackage("internal", "demo", [_release, _retired], _key);

            var releases = HexRegistry.ReadPackage(HexRegistry.ReadPayload(resource, _key), "demo");

            Assert.Collection(
                releases,
                r =>
                {
                    Assert.Equal("1.0.0", r.Version);
                    Assert.Equal(Checksum(1), r.InnerChecksum);
                    Assert.Equal(Checksum(2), r.OuterChecksum);
                    Assert.Null(r.Retirement);
                    Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), r.PublishedAt);
                    Assert.Collection(
                        r.Requirements,
                        d =>
                        {
                            Assert.Equal("jason", d.Name);
                            Assert.Equal("~> 1.4", d.Requirement);
                            Assert.False(d.Optional);
                            Assert.Null(d.App);
                            Assert.Null(d.Repository);
                        },
                        d =>
                        {
                            Assert.Equal("plug", d.Name);
                            Assert.True(d.Optional);
                            Assert.Equal("plug_app", d.App);
                            Assert.Equal("hexpm", d.Repository);
                        });
                },
                r =>
                {
                    Assert.Equal("1.1.0-rc.1", r.Version);
                    Assert.Equal("security", r.Retirement.Reason);
                    Assert.Equal("Use 1.0.0", r.Retirement.Message);
                    Assert.Null(r.PublishedAt);
                });
        }

        [Fact]
        public void NamesTheRepository()
        {
            var payload = HexRegistry.ReadPayload(HexRegistry.BuildPackage("internal", "demo", [_release], _key));

            Assert.Equal(["demo", "internal"], ReadStrings(payload, 2, 3));
        }

        [Fact]
        public void IsTheSameForTheSameContent()
        {
            Assert.Equal(
                HexRegistry.BuildPackage("internal", "demo", [_release], _key),
                HexRegistry.BuildPackage("internal", "demo", [_release], _key));
        }

        [Fact]
        public void RejectsAnotherPackage()
        {
            var payload = HexRegistry.ReadPayload(HexRegistry.BuildPackage("internal", "demo", [_release], _key));

            Assert.Throws<InvalidDataException>(() => HexRegistry.ReadPackage(payload, "other"));
        }
    }

    public class ReadPayload
    {
        [Fact]
        public void RejectsTheSignatureOfAnotherKey()
        {
            using var otherKey = RSA.Create(2048);
            var resource = HexRegistry.BuildPackage("internal", "demo", [_release], otherKey);

            Assert.Throws<InvalidDataException>(() => HexRegistry.ReadPayload(resource, _key));
        }

        [Fact]
        public void RejectsWhatIsNotARegistryResource()
        {
            Assert.Throws<InvalidDataException>(() => HexRegistry.ReadPayload([1, 2, 3]));
            Assert.Throws<InvalidDataException>(() => HexRegistry.ReadPayload(Gzip([0xFF, 0xFF])));
            Assert.Throws<InvalidDataException>(() => HexRegistry.ReadPayload(Gzip([])));
        }
    }

    public class BuildNamesAndVersions
    {
        [Fact]
        public void ListThePackagesWithTheirVersions()
        {
            var packages = new List<KeyValuePair<string, IReadOnlyList<HexRelease>>>
            {
                KeyValuePair.Create<string, IReadOnlyList<HexRelease>>("demo", [_release, _retired]),
                KeyValuePair.Create<string, IReadOnlyList<HexRelease>>("other", [_retired]),
            };

            var names = HexRegistry.ReadPayload(HexRegistry.BuildNames("internal", packages, _key), _key);
            var versions = HexRegistry.ReadPayload(HexRegistry.BuildVersions("internal", packages, _key), _key);

            // Names: the packages, then the repository.
            var nameEntries = ReadMessages(names, 1);
            Assert.Equal(2, nameEntries.Count);
            Assert.Equal(["demo"], ReadStrings(nameEntries[0], 1));
            Assert.Equal(["internal"], ReadStrings(names, 2));

            // Versions: each package with its versions and the indexes of the retired ones.
            var versionEntries = ReadMessages(versions, 1);
            Assert.Equal(["demo", "1.0.0", "1.1.0-rc.1"], ReadStrings(versionEntries[0], 1, 2));
            Assert.Equal([1UL], ReadVarints(versionEntries[0], 3));
            Assert.Equal([0UL], ReadVarints(versionEntries[1], 3));
            Assert.Equal(["internal"], ReadStrings(versions, 2));
        }
    }

    private static byte[] Checksum(byte fill)
    {
        var checksum = new byte[32];
        Array.Fill(checksum, fill);
        return checksum;
    }

    private static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    private static List<byte[]> ReadMessages(byte[] message, int field)
    {
        var reader = new ProtobufReader(message);
        var result = new List<byte[]>();
        while (reader.Next())
        {
            if (reader.Field == field)
            {
                result.Add(reader.ReadBytes());
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }

    private static List<string> ReadStrings(byte[] message, params int[] fields)
    {
        var reader = new ProtobufReader(message);
        var result = new List<string>();
        while (reader.Next())
        {
            if (Array.IndexOf(fields, reader.Field) >= 0 && reader.WireType == ProtobufReader.LengthDelimitedType)
            {
                result.Add(reader.ReadString());
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }

    private static List<ulong> ReadVarints(byte[] message, int field)
    {
        var reader = new ProtobufReader(message);
        var result = new List<ulong>();
        while (reader.Next())
        {
            if (reader.Field == field)
            {
                result.AddRange(reader.ReadVarints());
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }
}
