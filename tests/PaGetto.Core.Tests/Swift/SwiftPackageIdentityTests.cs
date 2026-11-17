using PaGetto.Core.Swift;
using Xunit;

namespace PaGetto.Core.Tests.Swift;

public class SwiftPackageIdentityTests
{
    public class IsValidScope
    {
        [Theory]
        [InlineData("mona", true)]
        [InlineData("Mona-Lisa", true)]
        [InlineData("a1", true)]
        [InlineData("-mona", false)]
        [InlineData("mona-", false)]
        [InlineData("mo--na", false)]
        [InlineData("mo_na", false)]
        [InlineData("mo.na", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AcceptsLettersDigitsAndSingleHyphens(string scope, bool valid)
        {
            Assert.Equal(valid, SwiftPackageIdentity.IsValidScope(scope));
        }

        [Fact]
        public void AcceptsAtMost39Characters()
        {
            Assert.True(SwiftPackageIdentity.IsValidScope(new string('a', 39)));
            Assert.False(SwiftPackageIdentity.IsValidScope(new string('a', 40)));
        }
    }

    public class IsValidName
    {
        [Theory]
        [InlineData("LinkedList", true)]
        [InlineData("linked_list", true)]
        [InlineData("linked-list", true)]
        [InlineData("_linked", false)]
        [InlineData("linked__list", false)]
        [InlineData("linked-_list", false)]
        [InlineData("linked.list", false)]
        [InlineData("linked list", false)]
        [InlineData(null, false)]
        public void AcceptsLettersDigitsAndSingleSeparators(string name, bool valid)
        {
            Assert.Equal(valid, SwiftPackageIdentity.IsValidName(name));
        }

        [Fact]
        public void AcceptsAtMost100Characters()
        {
            Assert.True(SwiftPackageIdentity.IsValidName(new string('a', 100)));
            Assert.False(SwiftPackageIdentity.IsValidName(new string('a', 101)));
        }
    }

    public class TryParse
    {
        [Fact]
        public void SplitsScopeAndName()
        {
            Assert.True(SwiftPackageIdentity.TryParse("mona.LinkedList", out var scope, out var name));
            Assert.Equal("mona", scope);
            Assert.Equal("LinkedList", name);
        }

        [Theory]
        [InlineData("LinkedList")]
        [InlineData("mona.Linked.List")]
        [InlineData(".LinkedList")]
        [InlineData("mona.")]
        [InlineData(null)]
        public void RefusesAnythingElse(string identifier)
        {
            Assert.False(SwiftPackageIdentity.TryParse(identifier, out _, out _));
        }
    }

    public class Normalize
    {
        [Fact]
        public void LowercasesTheIdentifier()
        {
            Assert.Equal("mona.linkedlist", SwiftPackageIdentity.Normalize("Mona", "LinkedList"));
        }
    }
}
