using System;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.S3;
using PaGetto.Core;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using PaGetto.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace PaGetto.Aws;

public static class AwsApplicationExtensions
{
    public static PaGettoApplication AddAwsS3Storage(this PaGettoApplication app)
    {
        app.Services.AddPaGettoOptions<S3StorageOptions>(nameof(PaGettoOptions.Storage));

        app.Services.AddTransient<S3StorageService>();
        app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<S3StorageService>());

        app.Services.AddProvider<IStorageService>((provider, config) =>
        {
            if (!config.HasStorageType("AwsS3")) return null;

            return provider.GetRequiredService<S3StorageService>();
        });

        app.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

            // With a custom endpoint, Region only sets the signing region, for services that can't derive it from the URL.
            var config = options.Endpoint != null
                ? new AmazonS3Config
                {
                    ServiceURL = options.Endpoint.AbsoluteUri,
                    ForcePathStyle = options.ForcePathStyle,
                    AuthenticationRegion = string.IsNullOrEmpty(options.Region) ? null : options.Region,
                }
                : new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) };

            if (options.UseInstanceProfile)
            {
                var credentials = DefaultAWSCredentialsIdentityResolver.GetCredentials(config);
                return new AmazonS3Client(credentials, config);
            }

            if (!string.IsNullOrEmpty(options.AssumeRoleArn))
            {
                var credentials = DefaultAWSCredentialsIdentityResolver.GetCredentials(config);
                var assumedCredentials = AssumeRoleAsync(
                        credentials,
                        options.AssumeRoleArn,
                        $"PaGetto-Session-{Guid.NewGuid()}")
                    .GetAwaiter()
                    .GetResult();

                return new AmazonS3Client(assumedCredentials, config);
            }

            if (!string.IsNullOrEmpty(options.AccessKey))
            {
                return new AmazonS3Client(
                    new BasicAWSCredentials(
                        options.AccessKey,
                        options.SecretKey),
                    config);
            }

            return new AmazonS3Client(config);
        });

        return app;
    }

    public static PaGettoApplication AddAwsS3Storage(
        this PaGettoApplication app,
        Action<S3StorageOptions> configure)
    {
        app.AddAwsS3Storage();
        app.Services.Configure(configure);
        return app;
    }

    private static async Task<AWSCredentials> AssumeRoleAsync(
        AWSCredentials credentials,
        string roleArn,
        string roleSessionName)
    {
        var assumedCredentials = new AssumeRoleAWSCredentials(credentials, roleArn, roleSessionName);
        var immutableCredentials = await credentials.GetCredentialsAsync();

        if (string.IsNullOrWhiteSpace(immutableCredentials.Token))
        {
            throw new InvalidOperationException($"Unable to assume role {roleArn}");
        }

        return assumedCredentials;
    }
}
