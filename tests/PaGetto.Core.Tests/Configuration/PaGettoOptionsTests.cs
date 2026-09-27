using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using PaGetto.Core.Configuration;
using Xunit;

namespace PaGetto.Core.Tests.Configuration;

public class PaGettoOptionsTests
{
    private static PaGettoOptions BindOptions(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .Get<PaGettoOptions>();
    }

    public class Binding
    {
        [Fact]
        public void DefaultsHealthCheckWhenSectionIsMissing()
        {
            var options = BindOptions(new Dictionary<string, string> { ["Database:Type"] = "Sqlite" });

            Assert.NotNull(options.HealthCheck);
            Assert.Equal("/health", options.HealthCheck.Path);
        }

        [Fact]
        public void DefaultsStatisticsWhenSectionIsMissing()
        {
            var options = BindOptions(new Dictionary<string, string> { ["Database:Type"] = "Sqlite" });

            Assert.NotNull(options.Statistics);
        }

        [Fact]
        public void UsesConfiguredHealthCheckPath()
        {
            var options = BindOptions(new Dictionary<string, string> { ["HealthCheck:Path"] = "/healthz" });

            Assert.Equal("/healthz", options.HealthCheck.Path);
        }
    }
}
