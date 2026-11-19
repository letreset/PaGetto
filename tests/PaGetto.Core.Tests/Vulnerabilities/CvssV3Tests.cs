using PaGetto.Core.Entities;
using PaGetto.Core.Vulnerabilities;
using Xunit;

namespace PaGetto.Core.Tests.Vulnerabilities;

public class CvssV3Tests
{
    public class TryGetBaseScore
    {
        // Scores from the FIRST CVSS v3.1 calculator.
        [Theory]
        [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H", 9.8)]
        [InlineData("CVSS:3.0/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H", 10.0)]
        [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:C/C:L/I:L/A:N", 6.1)]
        [InlineData("CVSS:3.1/AV:L/AC:H/PR:H/UI:R/S:U/C:L/I:N/A:N", 1.8)]
        [InlineData("CVSS:3.1/AV:N/AC:H/PR:L/UI:N/S:C/C:H/I:L/A:N", 7.1)]
        [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:N", 0.0)]
        public void ComputesTheBaseScore(string vector, double expected)
        {
            Assert.True(CvssV3.TryGetBaseScore(vector, out var score));
            Assert.Equal(expected, score);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:H/VI:H/VA:H/SC:N/SI:N/SA:N")]
        [InlineData("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/C:H/I:H/A:H")]
        [InlineData("CVSS:3.1/AV:X/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H")]
        [InlineData("AV:N/AC:L/Au:N/C:P/I:P/A:P")]
        public void RefusesWhatIsNotACompleteV3Vector(string vector)
        {
            Assert.False(CvssV3.TryGetBaseScore(vector, out _));
        }
    }

    public class GetSeverity
    {
        [Theory]
        [InlineData(10.0, VulnerabilitySeverity.Critical)]
        [InlineData(9.0, VulnerabilitySeverity.Critical)]
        [InlineData(8.9, VulnerabilitySeverity.High)]
        [InlineData(7.0, VulnerabilitySeverity.High)]
        [InlineData(6.9, VulnerabilitySeverity.Moderate)]
        [InlineData(4.0, VulnerabilitySeverity.Moderate)]
        [InlineData(3.9, VulnerabilitySeverity.Low)]
        [InlineData(0.0, VulnerabilitySeverity.Low)]
        public void UsesTheCvssRatings(double score, VulnerabilitySeverity expected)
        {
            Assert.Equal(expected, CvssV3.GetSeverity(score));
        }
    }
}
