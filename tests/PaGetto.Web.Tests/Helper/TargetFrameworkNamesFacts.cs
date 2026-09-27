using PaGetto.Web.Helper;
using Xunit;

namespace PaGetto.Web.Tests.Helper;

public class TargetFrameworkNamesFacts
{
    public class GetDisplayName
    {
        [Theory]
        [InlineData(null, "All Frameworks")]
        [InlineData("net10.0", ".NET 10.0")]
        [InlineData("net11.0", ".NET 11.0")]
        [InlineData("net8.0-windows", ".NET 8.0 (windows)")]
        [InlineData("netcoreapp3.1", ".NET Core 3.1")]
        [InlineData("netstandard2.0", ".NET Standard 2.0")]
        [InlineData("net20", ".NET Framework 2.0")]
        [InlineData("net40", ".NET Framework 4.0")]
        [InlineData("net403", ".NET Framework 4.0.3")]
        [InlineData("net47", ".NET Framework 4.7")]
        [InlineData("net472", ".NET Framework 4.7.2")]
        [InlineData("portable-net40+sl5+win8+wp8+wpa81", ".NET Portable (net40, sl5, win8, wp8, wpa81)")]
        [InlineData("portable-net45+win8+wp8+wpa81", ".NET Portable (net45, win8, wp8, wpa81)")]
        [InlineData("portable-wpa81+net45+win8+wp8", ".NET Portable (net45, win8, wp8, wpa81)")]
        [InlineData("not a framework", "not a framework")]
        public void ReturnsReadableName(string moniker, string expected)
        {
            Assert.Equal(expected, TargetFrameworkNames.GetDisplayName(moniker));
        }
    }

    public class Sort
    {
        [Fact]
        public void OrdersByFamilyThenNewestVersion()
        {
            var sorted = TargetFrameworkNames.Sort(
                ["net20", "net10.0", "netstandard2.0", "net35", "net5.0", "netcoreapp3.1", "net48", "netstandard1.3", "garbage", "net8.0"]);

            Assert.Equal(
                ["net10.0", "net8.0", "net5.0", "netcoreapp3.1", "netstandard2.0", "netstandard1.3", "net48", "net35", "net20", "garbage"],
                sorted);
        }
    }

    public class GetLowestPerFamily
    {
        [Fact]
        public void KeepsTheLowestVersionPerFamilyInFamilyOrder()
        {
            var badges = TargetFrameworkNames.GetLowestPerFamily(["net8.0", "net20", "netstandard2.0", "net6.0"]);

            Assert.Equal([".NET 6.0", ".NET Standard 2.0", ".NET Framework 2.0"], badges);
        }

        [Fact]
        public void KeepsDotNetAndDotNetCoreApart()
        {
            var badges = TargetFrameworkNames.GetLowestPerFamily(["net8.0", "netcoreapp3.1"]);

            Assert.Equal([".NET 8.0", ".NET Core 3.1"], badges);
        }

        [Fact]
        public void PrefersTheFrameworkWithoutPlatform()
        {
            var badges = TargetFrameworkNames.GetLowestPerFamily(["net8.0-windows", "net8.0"]);

            Assert.Equal([".NET 8.0"], badges);
        }

        [Fact]
        public void SkipsAnyAndUnparseableMonikers()
        {
            Assert.Empty(TargetFrameworkNames.GetLowestPerFamily(["any", "not a framework"]));
        }
    }
}
