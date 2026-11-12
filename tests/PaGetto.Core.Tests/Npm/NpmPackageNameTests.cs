using PaGetto.Core.Npm;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmPackageNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("lodash")]
        [InlineData("socket.io")]
        [InlineData("left_pad")]
        [InlineData("@babel/core")]
        [InlineData("@my-org/ui.kit")]
        [InlineData("JSONStream")]
        public void AcceptsNpmNames(string name)
        {
            Assert.True(NpmPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".hidden")]
        [InlineData("_private")]
        [InlineData("has space")]
        [InlineData("a/b")]
        [InlineData("@scope")]
        [InlineData("@scope/")]
        [InlineData("@scope/a/b")]
        [InlineData("@*/name")]
        [InlineData("..")]
        [InlineData("name%2f..")]
        public void RejectsAnythingElse(string name)
        {
            Assert.False(NpmPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsNamesLongerThanTheDatabaseColumn()
        {
            Assert.True(NpmPackageName.IsValid(new string('a', NpmPackageName.MaxLength)));
            Assert.False(NpmPackageName.IsValid(new string('a', NpmPackageName.MaxLength + 1)));
        }
    }

    public class IsValidForPublish
    {
        [Fact]
        public void RequiresALowercaseName()
        {
            Assert.True(NpmPackageName.IsValidForPublish("@scope/name"));
            Assert.False(NpmPackageName.IsValidForPublish("JSONStream"));
        }
    }

    public class FromUrlSegment
    {
        [Theory]
        [InlineData("lodash", "lodash")]
        [InlineData("@scope%2fname", "@scope/name")]
        [InlineData("@scope%2Fname", "@scope/name")]
        public void DecodesTheSlashOfAScopedName(string segment, string name)
        {
            Assert.Equal(name, NpmPackageName.FromUrlSegment(segment));
        }
    }

    public class Escape
    {
        [Theory]
        [InlineData("lodash", "lodash")]
        [InlineData("@scope/name", "@scope%2Fname")]
        public void EncodesTheSlashOfAScopedName(string name, string segment)
        {
            Assert.Equal(segment, NpmPackageName.Escape(name));
        }
    }

    public class TarballFileName
    {
        [Theory]
        [InlineData("lodash", "4.17.21", "lodash-4.17.21.tgz")]
        [InlineData("@scope/name", "1.0.0-beta.1", "name-1.0.0-beta.1.tgz")]
        public void IsTheUnscopedNameAndVersion(string name, string version, string fileName)
        {
            Assert.Equal(fileName, NpmPackageName.GetTarballFileName(name, version));

            Assert.True(NpmPackageName.TryGetVersionFromTarballFileName(name, fileName, out var parsed));
            Assert.Equal(version, parsed);
        }

        [Theory]
        [InlineData("lodash", "other-1.0.0.tgz")]
        [InlineData("lodash", "lodash-1.0.0.tar.gz")]
        [InlineData("lodash", "lodash-.tgz")]
        [InlineData("lodash", null)]
        public void RejectsFileNamesOfOtherPackages(string name, string fileName)
        {
            Assert.False(NpmPackageName.TryGetVersionFromTarballFileName(name, fileName, out _));
        }
    }
}
