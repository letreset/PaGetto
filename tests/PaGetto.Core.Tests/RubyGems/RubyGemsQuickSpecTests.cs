using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PaGetto.Core.RubyGems;
using PaGetto.Core.Tests.Support;
using Xunit;
using static PaGetto.Core.Tests.Support.RubyMarshalReader;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsQuickSpecTests
{
    public class Create
    {
        [Fact]
        public void IsAZlibCompressedMarshalDumpOfTheSpecification()
        {
            var specification = new RubyGemsSpecification
            {
                Name = "my_gem",
                Version = "1.2.0",
                Platform = "x86_64-linux",
                Summary = "A test gem.",
                Description = "Ünïcode.",
                Authors = ["Test Author"],
                Email = ["test@example.com"],
                Homepage = "https://example.com",
                Licenses = ["MIT"],
                Metadata = new Dictionary<string, string> { ["source_code_uri"] = "https://example.com/src" },
                RequiredRubyVersion = [">= 3.0"],
                Dependencies =
                [
                    new RubyGemsDependency { Name = "rake", Requirement = ["~> 13.0", ">= 13.0.1"] },
                    new RubyGemsDependency { Name = "rspec", Type = RubyGemsDependency.DevelopmentType },
                ],
                Date = "2020-01-01",
                RubygemsVersion = "3.5.3",
                SpecificationVersion = 4,
            };

            using var zlib = new ZLibStream(new MemoryStream(RubyGemsQuickSpec.Create(specification)), CompressionMode.Decompress);
            using var marshal = new MemoryStream();
            zlib.CopyTo(marshal);

            // Gem::Specification has _dump, which is the Marshal data of an array of its fields.
            var spec = Assert.IsType<RubyObject>(Read(marshal.ToArray()));
            Assert.Equal(('u', "Gem::Specification"), (spec.Type, spec.ClassName));

            var fields = Assert.IsType<List<object>>(Read((byte[])spec.Data));
            Assert.Equal(19, fields.Count);
            Assert.Equal("3.5.3", fields[0]);
            Assert.Equal(4, fields[1]);
            Assert.Equal("my_gem", fields[2]);
            Assert.Equal("1.2.0", Version(fields[3]));
            Assert.Equal("2020-01-01", fields[4]);
            Assert.Equal("A test gem.", fields[5]);
            Assert.Equal(new[] { ">= 3.0" }, Requirement(fields[6]));
            Assert.Equal(new[] { ">= 0" }, Requirement(fields[7]));
            Assert.Equal("x86_64-linux", fields[8]);

            var dependencies = Assert.IsType<List<object>>(fields[9]).Cast<RubyObject>().ToList();
            Assert.Equal(2, dependencies.Count);
            Assert.All(dependencies, d => Assert.Equal(('o', "Gem::Dependency"), (d.Type, d.ClassName)));
            Assert.Equal("rake", dependencies[0].InstanceVariables["@name"]);
            Assert.Equal(new[] { "~> 13.0", ">= 13.0.1" }, Requirement(dependencies[0].InstanceVariables["@requirement"]));
            Assert.Equal(new Symbol("runtime"), dependencies[0].InstanceVariables["@type"]);
            Assert.Equal(false, dependencies[0].InstanceVariables["@prerelease"]);
            Assert.Equal(new[] { ">= 0" }, Requirement(dependencies[1].InstanceVariables["@requirement"]));
            Assert.Equal(new Symbol("development"), dependencies[1].InstanceVariables["@type"]);

            Assert.Equal("", fields[10]);
            Assert.Equal(new object[] { "test@example.com" }, fields[11]);
            Assert.Equal(new object[] { "Test Author" }, fields[12]);
            Assert.Equal("Ünïcode.", fields[13]);
            Assert.Equal("https://example.com", fields[14]);
            Assert.Equal(true, fields[15]);
            Assert.Equal("x86_64-linux", fields[16]);
            Assert.Equal(new object[] { "MIT" }, fields[17]);
            Assert.Equal("https://example.com/src", Assert.IsType<Dictionary<object, object>>(fields[18])["source_code_uri"]);
        }

        [Fact]
        public void RubyPlatformAndMissingFieldsHaveDefaults()
        {
            var specification = new RubyGemsSpecification { Name = "tiny", Version = "0.1.0", Platform = null };

            using var zlib = new ZLibStream(new MemoryStream(RubyGemsQuickSpec.Create(specification)), CompressionMode.Decompress);
            using var marshal = new MemoryStream();
            zlib.CopyTo(marshal);

            var fields = (List<object>)Read((byte[])((RubyObject)Read(marshal.ToArray())).Data);
            Assert.Equal("0", fields[0]);
            Assert.Equal(4, fields[1]);
            Assert.Null(fields[4]);
            Assert.Null(fields[5]);
            Assert.Equal("ruby", fields[8]);
            Assert.Empty((List<object>)fields[9]);
            Assert.Equal("ruby", fields[16]);
        }

        private static string Version(object value)
        {
            var version = Assert.IsType<RubyObject>(value);
            Assert.Equal(('U', "Gem::Version"), (version.Type, version.ClassName));
            return (string)Assert.Single((List<object>)version.Data);
        }

        private static string[] Requirement(object value)
        {
            var requirement = Assert.IsType<RubyObject>(value);
            Assert.Equal(('U', "Gem::Requirement"), (requirement.Type, requirement.ClassName));

            var pairs = (List<object>)Assert.Single((List<object>)requirement.Data);
            return pairs.Cast<List<object>>().Select(p => $"{p[0]} {Version(p[1])}").ToArray();
        }
    }
}
