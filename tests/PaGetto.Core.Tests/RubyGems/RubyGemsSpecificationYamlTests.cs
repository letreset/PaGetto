using System.Linq;
using PaGetto.Core.RubyGems;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsSpecificationYamlTests
{
    public class Parse
    {
        [Fact]
        public void ReadsTheGemspecRubyGemsWrites()
        {
            var yaml = RubyGemBuilder.Gemspec(
                "my_gem",
                "1.2.0",
                "x86_64-linux",
                [("rake", "~> 13.0", "runtime"), ("json", ">= 2.0, < 3", "runtime"), ("rspec", "~> 3.0", "development"), ("any", ">= 0", "runtime")],
                ">= 3.0");

            var specification = RubyGemsSpecificationYaml.Parse(yaml);

            Assert.Equal("my_gem", specification.Name);
            Assert.Equal("1.2.0", specification.Version);
            Assert.Equal("x86_64-linux", specification.Platform);
            Assert.Equal("A test gem.", specification.Summary);
            Assert.Equal("A longer description of my_gem.", specification.Description);
            Assert.Equal(new[] { "Test Author" }, specification.Authors);
            Assert.Equal(new[] { "test@example.com" }, specification.Email);
            Assert.Equal("https://example.com/my_gem", specification.Homepage);
            Assert.Equal(new[] { "MIT" }, specification.Licenses);
            Assert.Equal("https://example.com/my_gem/source", specification.Metadata["source_code_uri"]);
            Assert.Equal(new[] { ">= 3.0" }, specification.RequiredRubyVersion);
            Assert.Empty(specification.RequiredRubygemsVersion);
            Assert.Equal("2020-01-01", specification.Date);
            Assert.Equal("3.5.3", specification.RubygemsVersion);
            Assert.Equal(4, specification.SpecificationVersion);

            Assert.Equal(
                new[] { "rake ~> 13.0 runtime", "json >= 2.0&< 3 runtime", "rspec ~> 3.0 development", "any  runtime" },
                specification.Dependencies.Select(d => $"{d.Name} {string.Join('&', d.Requirement)} {d.Type}"));
        }

        [Fact]
        public void ReadsAMinimalGemspec()
        {
            var specification = RubyGemsSpecificationYaml.Parse("--- !ruby/object:Gem::Specification\nname: tiny\nversion: 0.1.0\nemail: one@example.com\n");

            Assert.Equal("tiny", specification.Name);
            Assert.Equal("0.1.0", specification.Version);
            Assert.Equal("ruby", specification.Platform);
            Assert.Equal(new[] { "one@example.com" }, specification.Email);
            Assert.Empty(specification.Dependencies);
            Assert.Null(specification.Summary);
        }

        [Fact]
        public void DecodesBinaryText()
        {
            var specification = RubyGemsSpecificationYaml.Parse("name: tiny\nversion: '1.0'\nsummary: !binary |-\n  w5xiZXI=\n");

            Assert.Equal("Über", specification.Summary);
        }

        [Theory]
        [InlineData("not: [valid")]
        [InlineData("- a list")]
        [InlineData("name: tiny\n")]
        [InlineData("version: 1.0\n")]
        [InlineData("name: tiny\nversion: '1.0'\ndependencies:\n- !ruby/object:Gem::Dependency\n  type: :runtime\n")]
        [InlineData("name: tiny\nversion: '1.0'\nrequired_ruby_version: !ruby/object:Gem::Requirement\n  requirements:\n  - - \">=\"\n")]
        public void RejectsWhatIsNotAGemspec(string yaml)
        {
            Assert.Throws<InvalidRubyGemException>(() => RubyGemsSpecificationYaml.Parse(yaml));
        }
    }
}
