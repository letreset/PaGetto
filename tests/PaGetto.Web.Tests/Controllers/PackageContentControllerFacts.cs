using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Content;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Web.Tests.Controllers;

public class PackageContentControllerFacts
{
    public class DownloadIconAsync : FactsBase
    {
        [Theory]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
        [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
        [InlineData(new byte[] { 0x42, 0x4D, 0x00 }, "image/bmp")]
        [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
        [InlineData(new byte[] { 0x01, 0x02, 0x03 }, "application/octet-stream")]
        public async Task DetectsContentTypeFromIconBytes(byte[] icon, string expectedContentType)
        {
            Content
                .Setup(c => c.GetPackageIconStreamOrNullAsync(Feed.Id, Feed.Slug, "test", It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(icon));

            var result = await Build().DownloadIconAsync("test", "1.0.0", CancellationToken.None);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal(expectedContentType, file.ContentType);
            Assert.Equal(icon, file.FileContents);
        }

        [Fact]
        public async Task ReturnsNotFoundWhenIconMissing()
        {
            Content
                .Setup(c => c.GetPackageIconStreamOrNullAsync(Feed.Id, Feed.Slug, "test", It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream)null);

            var result = await Build().DownloadIconAsync("test", "1.0.0", CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }
    }

    public class FactsBase
    {
        protected readonly Feed Feed = new() { Id = Guid.NewGuid(), Slug = "default" };
        protected readonly Mock<IPackageContentService> Content = new();
        protected readonly Mock<IFeedContext> FeedContext = new();

        protected PackageContentController Build()
        {
            FeedContext.Setup(f => f.CurrentFeed).Returns(Feed);
            return new PackageContentController(Content.Object, FeedContext.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }
    }
}
