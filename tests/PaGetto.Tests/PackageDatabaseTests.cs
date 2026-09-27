using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Feeds;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;
using PaGettoApplication = PaGetto.Tests.Support.PaGettoApplication;

namespace PaGetto.Tests;

public class PackageDatabaseTests
{
    public class AddDownloadAsync : FactsBase
    {
        public AddDownloadAsync(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task CountsEveryConcurrentDownload()
        {
            const int downloads = 200;
            await AddPackageAsync();
            var feedId = await GetDefaultFeedIdAsync();

            await Task.WhenAll(Enumerable.Range(0, downloads).Select(_ => Task.Run(async () =>
            {
                using var scope = App.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
                await db.AddDownloadAsync(feedId, PackageId, PackageVersion, CancellationToken.None);
            })));

            Assert.Equal(downloads, await GetDownloadsAsync(feedId));
        }

        [Fact]
        public async Task DoesNothing_WhenPackageDoesNotExist()
        {
            var feedId = await GetDefaultFeedIdAsync();

            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            await db.AddDownloadAsync(feedId, PackageId, PackageVersion, CancellationToken.None);

            Assert.False(await db.ExistsAsync(feedId, PackageId, CancellationToken.None));
        }
    }

    public abstract class FactsBase : IDisposable
    {
        protected const string PackageId = "TestData";
        protected static readonly NuGetVersion PackageVersion = NuGetVersion.Parse("1.2.3");

        protected FactsBase(ITestOutputHelper output)
        {
            App = new PaGettoApplication(output);
        }

        protected PaGettoApplication App { get; }

        protected async Task AddPackageAsync()
        {
            using var stream = TestResources.GetResourceStream(TestResources.Package);
            await App.AddPackageAsync(stream);
        }

        protected async Task<Guid> GetDefaultFeedIdAsync()
        {
            using var scope = App.Services.CreateScope();
            var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();
            var feed = await feedService.GetDefaultFeedAsync(CancellationToken.None);
            return feed.Id;
        }

        protected async Task<long> GetDownloadsAsync(Guid feedId)
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IPackageDatabase>();
            var package = await db.FindOrNullAsync(feedId, PackageId, PackageVersion, includeUnlisted: true, CancellationToken.None);
            return package.Downloads;
        }

        public void Dispose()
        {
            App.Dispose();
        }
    }
}
