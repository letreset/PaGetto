using System.Linq;
using PaGetto.Core.RubyGems;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsFileNameTests
{
    public class Parse
    {
        [Theory]
        [InlineData("rake-13.0.6", "rake", "13.0.6", "ruby")]
        [InlineData("net-http-0.4.1", "net-http", "0.4.1", "ruby")]
        [InlineData("nokogiri-1.16.0-x86_64-linux", "nokogiri", "1.16.0", "x86_64-linux")]
        [InlineData("nokogiri-1.16.0-arm64-darwin", "nokogiri", "1.16.0", "arm64-darwin")]
        [InlineData("jruby-openssl-0.14.2-java", "jruby-openssl", "0.14.2", "java")]
        [InlineData("RedCloth-4.3.2", "RedCloth", "4.3.2", "ruby")]
        [InlineData("rails-7.1.0.rc1", "rails", "7.1.0.rc1", "ruby")]
        public void SplitsAFullName(string fullName, string name, string version, string platform)
        {
            var parsed = Assert.Single(RubyGemsFileName.Parse(fullName));

            Assert.Equal(name, parsed.Name);
            Assert.Equal(version, parsed.Version);
            Assert.Equal(platform, parsed.Platform);
            Assert.Equal(fullName, parsed.FullName);
        }

        [Fact]
        public void NameWithADashAndADigitHasEverySplit()
        {
            var parsed = RubyGemsFileName.Parse("ab-1-x-2");

            Assert.Equal(new[] { ("ab", "1", "x-2"), ("ab-1-x", "2", "ruby") }, parsed.Select(p => (p.Name, p.Version, p.Platform)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("rake")]
        [InlineData("rake-")]
        [InlineData("1.0-1.0")]
        [InlineData("rake-13.0.6-ruby")]
        [InlineData("rake-13.0.6-")]
        [InlineData("../rake-13.0.6")]
        [InlineData("rake-13.0.6\n")]
        public void FindsNothingInWhatIsNotAFullName(string fullName)
        {
            Assert.Empty(RubyGemsFileName.Parse(fullName));
        }
    }

    public class GetFullName
    {
        [Theory]
        [InlineData("ruby", "tool-1.0.0")]
        [InlineData(null, "tool-1.0.0")]
        [InlineData("java", "tool-1.0.0-java")]
        public void LeavesOutTheRubyPlatform(string platform, string fullName)
        {
            Assert.Equal(fullName, RubyGemsFileName.GetFullName("tool", "1.0.0", platform));
        }
    }

    public class TrimExtension
    {
        [Theory]
        [InlineData("rake-13.0.6.gem", ".gem", "rake-13.0.6")]
        [InlineData("rake-13.0.6.gemspec.rz", ".gemspec.rz", "rake-13.0.6")]
        [InlineData("rake-13.0.6.tgz", ".gem", null)]
        [InlineData(".gem", ".gem", null)]
        public void RemovesTheExtension(string fileName, string extension, string expected)
        {
            Assert.Equal(expected, RubyGemsFileName.TrimExtension(fileName, extension));
        }
    }
}
