using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using PaGetto.Aws;
using Microsoft.Extensions.Options;
using Xunit;

namespace PaGetto.Tests;

public class S3StorageServiceTests
{
    public class PutAsync : FactsBase
    {
        [Fact]
        public async Task UsesChunkEncodingByDefault()
        {
            var request = await CapturePutRequestAsync(new S3StorageOptions { Bucket = "nuget-packages" });

            Assert.True(request.UseChunkEncoding);
        }

        [Fact]
        public async Task DisablesChunkEncodingWhenConfigured()
        {
            var request = await CapturePutRequestAsync(new S3StorageOptions { Bucket = "nuget-packages", UseChunkEncoding = false });

            Assert.False(request.UseChunkEncoding);
        }
    }

    public class FactsBase
    {
        protected static async Task<PutObjectRequest> CapturePutRequestAsync(S3StorageOptions options)
        {
            PutObjectRequest request = null;
            var client = new Mock<AmazonS3Client>(
                new BasicAWSCredentials("access", "secret"),
                new AmazonS3Config { ServiceURL = "http://localhost:9000" });
            client
                .Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutObjectRequest, CancellationToken>((r, _) => request = r)
                .ReturnsAsync(new PutObjectResponse());

            var snapshot = new Mock<IOptionsSnapshot<S3StorageOptions>>();
            snapshot.Setup(s => s.Value).Returns(options);

            var target = new S3StorageService(snapshot.Object, client.Object);
            using var content = new MemoryStream([1, 2, 3]);
            await target.PutAsync("packages/default/test/1.0.0/test.1.0.0.nupkg", content, "binary/octet-stream");

            return request;
        }
    }
}
