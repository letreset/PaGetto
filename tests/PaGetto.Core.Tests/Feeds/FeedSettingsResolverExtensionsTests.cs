using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Xunit;

namespace PaGetto.Core.Tests.Feeds;

public class FeedSettingsResolverExtensionsTests
{
    public class CanOverwrite
    {
        [Theory]
        [InlineData(PackageOverwriteAllowed.True, false, true)]
        [InlineData(PackageOverwriteAllowed.True, true, true)]
        [InlineData(PackageOverwriteAllowed.PrereleaseOnly, false, false)]
        [InlineData(PackageOverwriteAllowed.PrereleaseOnly, true, true)]
        [InlineData(PackageOverwriteAllowed.False, false, false)]
        [InlineData(PackageOverwriteAllowed.False, true, false)]
        public void FollowsTheFeedsOverwritePolicy(PackageOverwriteAllowed policy, bool isPrerelease, bool expected)
        {
            var feed = new Feed();
            var settings = new Mock<IFeedSettingsResolver>();
            settings.Setup(s => s.GetAllowPackageOverwrites(feed)).Returns(policy);

            Assert.Equal(expected, settings.Object.CanOverwrite(feed, isPrerelease));
        }
    }

    public class GetMaxPackageSizeBytes
    {
        [Fact]
        public void IsTheLimitInMiBAsBytes()
        {
            var feed = new Feed();
            var settings = new Mock<IFeedSettingsResolver>();
            settings.Setup(s => s.GetMaxPackageSizeMiB(feed)).Returns(8192);

            Assert.Equal(8192L * 1024 * 1024, settings.Object.GetMaxPackageSizeBytes(feed));
        }
    }
}
