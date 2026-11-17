using PaGetto.Core.CocoaPods;
using Xunit;

namespace PaGetto.Core.Tests.CocoaPods;

public class CocoaPodsPodNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("Alamofire")]
        [InlineData("lottie-ios")]
        [InlineData("gRPC-C++")]
        [InlineData("!ProtoCompiler")]
        [InlineData("@react-native-mapbox-gl-mapbox-static")]
        [InlineData("AFDateHelper(icanzilb)")]
        [InlineData("GPUImage@siuying")]
        [InlineData("Firebase.Core_2")]
        public void AcceptsPodNames(string name)
        {
            Assert.True(CocoaPodsPodName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".hidden")]
        [InlineData("Firebase/Core")]
        [InlineData("My Lib")]
        [InlineData("FLYKìt")]
        [InlineData("a%2Fb")]
        public void RejectsEverythingElse(string name)
        {
            Assert.False(CocoaPodsPodName.IsValid(name));
        }

        [Fact]
        public void RejectsLongNames()
        {
            Assert.False(CocoaPodsPodName.IsValid(new string('A', CocoaPodsPodName.MaxLength + 1)));
        }
    }

    public class Escape
    {
        [Theory]
        [InlineData("Alamofire", "!alamofire")]
        [InlineData("lottie-ios", "lottie-ios")]
        [InlineData("!ProtoCompiler", "!!!proto!compiler")]
        [InlineData("MyLib", "!my!lib")]
        public void KeepsTheCaseInALowercaseName(string name, string escaped)
        {
            Assert.Equal(escaped, CocoaPodsPodName.Escape(name));
            Assert.True(CocoaPodsPodName.TryUnescape(escaped, out var unescaped));
            Assert.Equal(name, unescaped);
        }

        [Fact]
        public void LongestNameFitsTheDatabaseColumn()
        {
            Assert.True(CocoaPodsPodName.Escape(new string('A', CocoaPodsPodName.MaxLength)).Length <= 128);
        }

        [Theory]
        [InlineData("MyLib")]
        [InlineData("my!")]
        [InlineData("my!1")]
        [InlineData("")]
        public void RejectsTextsThatAreNotEscaped(string escaped)
        {
            Assert.False(CocoaPodsPodName.TryUnescape(escaped, out _));
        }
    }

    public class GetShard
    {
        [Fact]
        public void UsesThePrefixesOfTheHashOfTheExactName()
        {
            // As cdn.cocoapods.org serves Specs/d/a/2/Alamofire/.
            Assert.Equal(new[] { "d", "a", "2" }, CocoaPodsPodName.GetShard("Alamofire"));
            Assert.NotEqual(CocoaPodsPodName.GetShard("Alamofire"), CocoaPodsPodName.GetShard("alamofire"));
        }
    }
}
