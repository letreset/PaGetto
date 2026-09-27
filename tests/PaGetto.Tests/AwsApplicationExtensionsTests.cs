using System.Collections.Generic;
using Amazon.S3;
using PaGetto.Aws;
using PaGetto.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PaGetto.Tests;

public class AwsApplicationExtensionsTests
{
    public class AddAwsS3Storage : FactsBase
    {
        [Fact]
        public void SetsForcePathStyleForCustomEndpoint()
        {
            var client = BuildClient(new Dictionary<string, string>
            {
                ["Storage:Endpoint"] = "http://localhost:9000",
                ["Storage:ForcePathStyle"] = "true",
            });

            Assert.True(((AmazonS3Config)client.Config).ForcePathStyle);
        }

        [Fact]
        public void DoesNotForcePathStyleByDefault()
        {
            var client = BuildClient(new Dictionary<string, string>
            {
                ["Storage:Endpoint"] = "http://localhost:9000",
            });

            Assert.False(((AmazonS3Config)client.Config).ForcePathStyle);
        }
    }

    public class FactsBase
    {
        protected static AmazonS3Client BuildClient(Dictionary<string, string> settings)
        {
            settings["Storage:Bucket"] = "nuget-packages";
            settings["Storage:AccessKey"] = "access";
            settings["Storage:SecretKey"] = "secret";

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddOptions();

            new PaGettoApplication(services).AddAwsS3Storage();

            return services.BuildServiceProvider().GetRequiredService<AmazonS3Client>();
        }
    }
}
