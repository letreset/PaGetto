using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaGetto.Core.Cargo;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoIndexEntryTests
{
    public class FromPublishMetadata
    {
        [Fact]
        public void ConvertsDependenciesToTheIndexForm()
        {
            var metadata = CargoCrateBuilder.Metadata("my_crate", "1.0.0");
            metadata["deps"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = "serde",
                    ["version_req"] = "^1.0",
                    ["features"] = new JsonArray("derive"),
                    ["optional"] = true,
                    ["default_features"] = false,
                    ["target"] = null,
                    ["kind"] = "normal",
                    ["registry"] = "https://github.com/rust-lang/crates.io-index",
                    ["explicit_name_in_toml"] = null,
                },
                new JsonObject
                {
                    ["name"] = "other_crate",
                    ["version_req"] = "=2.0.0",
                    ["kind"] = "dev",
                    ["explicit_name_in_toml"] = "renamed",
                });

            var manifest = CargoIndexEntry.FromPublishMetadata(metadata);

            var serde = manifest["deps"]![0]!.AsObject();
            Assert.Equal("serde", serde["name"]!.GetValue<string>());
            Assert.Equal("^1.0", serde["req"]!.GetValue<string>());
            Assert.Equal("derive", serde["features"]![0]!.GetValue<string>());
            Assert.True(serde["optional"]!.GetValue<bool>());
            Assert.False(serde["default_features"]!.GetValue<bool>());
            Assert.Equal("https://github.com/rust-lang/crates.io-index", serde["registry"]!.GetValue<string>());
            Assert.False(serde.ContainsKey("package"));

            // A renamed dependency is listed under its new name, with the crate as "package".
            var renamed = manifest["deps"]![1]!.AsObject();
            Assert.Equal("renamed", renamed["name"]!.GetValue<string>());
            Assert.Equal("other_crate", renamed["package"]!.GetValue<string>());
            Assert.Equal("dev", renamed["kind"]!.GetValue<string>());
            Assert.True(renamed["default_features"]!.GetValue<bool>());
            Assert.False(renamed.ContainsKey("registry"));
        }

        [Fact]
        public void MovesFeaturesWithTheNewSyntaxToFeatures2()
        {
            var metadata = CargoCrateBuilder.Metadata("my_crate", "1.0.0");
            metadata["features"] = new JsonObject
            {
                ["default"] = new JsonArray("std"),
                ["std"] = new JsonArray(),
                ["json"] = new JsonArray("dep:serde_json"),
                ["derive"] = new JsonArray("serde?/derive"),
            };

            var manifest = CargoIndexEntry.FromPublishMetadata(metadata);

            Assert.Equal(new[] { "default", "std" }, Keys(manifest["features"]!.AsObject()));
            Assert.Equal(new[] { "json", "derive" }, Keys(manifest["features2"]!.AsObject()));
            Assert.Equal(2, manifest["v"]!.GetValue<int>());
        }

        [Fact]
        public void KeepsThePackageFieldsForTheWebUi()
        {
            var metadata = CargoCrateBuilder.Metadata("my_crate", "1.0.0");
            metadata["repository"] = "https://example.com/repo";
            metadata["rust_version"] = "1.70";

            var manifest = CargoIndexEntry.FromPublishMetadata(metadata);

            Assert.Equal("A test crate.", manifest["description"]!.GetValue<string>());
            Assert.Equal("MIT", manifest["license"]!.GetValue<string>());
            Assert.Equal("https://example.com/repo", manifest["repository"]!.GetValue<string>());
            Assert.Equal("1.70", manifest["rust_version"]!.GetValue<string>());
            Assert.False(manifest.ContainsKey("readme"));
            Assert.False(manifest.ContainsKey("badges"));
        }

        [Fact]
        public void RejectsDependenciesWithoutAVersionRequirement()
        {
            var metadata = CargoCrateBuilder.Metadata("my_crate", "1.0.0");
            metadata["deps"] = new JsonArray(new JsonObject { ["name"] = "serde" });

            Assert.Throws<InvalidCargoPackageException>(() => CargoIndexEntry.FromPublishMetadata(metadata));
        }

        [Fact]
        public void RejectsFeaturesThatAreNotLists()
        {
            var metadata = CargoCrateBuilder.Metadata("my_crate", "1.0.0");
            metadata["features"] = new JsonObject { ["std"] = "yes" };

            Assert.Throws<InvalidCargoPackageException>(() => CargoIndexEntry.FromPublishMetadata(metadata));
        }

        private static string[] Keys(JsonObject value)
        {
            var keys = new string[value.Count];
            var i = 0;
            foreach (var (key, _) in value)
            {
                keys[i++] = key;
            }

            return keys;
        }
    }

    public class Write
    {
        [Fact]
        public void WritesTheIndexFieldsWithTheChecksumAndYankedState()
        {
            var manifest = CargoIndexEntry.FromPublishMetadata(CargoCrateBuilder.Metadata("My_Crate", "1.0.0"));

            var line = WriteLine(manifest.ToJsonString(), new string('a', 64), yanked: true);

            Assert.Equal(
                "{\"name\":\"My_Crate\",\"vers\":\"1.0.0\",\"deps\":[],\"cksum\":\"" + new string('a', 64)
                + "\",\"features\":{},\"yanked\":true,\"links\":null}",
                line);
        }

        [Fact]
        public void WritesAnUpstreamLineWithTheStoredChecksumAndState()
        {
            const string upstream = "{\"name\":\"serde\",\"vers\":\"1.0.0\",\"deps\":[],\"cksum\":\"old\",\"features\":{},\"features2\":{\"std\":[\"dep:x\"]},\"yanked\":false,\"links\":null,\"v\":2,\"rust_version\":\"1.31\",\"pubtime\":\"2024-01-01T00:00:00Z\"}";

            using var line = JsonDocument.Parse(WriteLine(upstream, "new", yanked: true));
            var root = line.RootElement;

            Assert.Equal("new", root.GetProperty("cksum").GetString());
            Assert.True(root.GetProperty("yanked").GetBoolean());
            Assert.Equal(2, root.GetProperty("v").GetInt32());
            Assert.Equal("1.31", root.GetProperty("rust_version").GetString());
            Assert.Equal("dep:x", root.GetProperty("features2").GetProperty("std")[0].GetString());
            Assert.False(root.TryGetProperty("pubtime", out _));
        }

        private static string WriteLine(string manifestJson, string cksum, bool yanked)
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                CargoIndexEntry.Write(writer, manifestJson, cksum, yanked);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
