using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Indexing;
using PaGetto.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class PackageMetadataBackfillTests : IDisposable
{
    private readonly PaGettoApplication _app;

    public PackageMetadataBackfillTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
    }

    [Fact]
    public async Task PushRecordsTheSize()
    {
        var expectedSize = TestResources.GetResourceStream(TestResources.Package).Length;
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));

        var package = await FindPackageAsync();

        Assert.Equal(expectedSize, package.Size);
    }

    [Fact]
    public async Task FillsPackagesStoredBeforeTheSizeWasRecorded()
    {
        var expectedSize = TestResources.GetResourceStream(TestResources.Package).Length;
        await _app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package));
        await UpdatePackageAsync(p => p.Size = null);

        var backfill = _app.Services.GetServices<IHostedService>().OfType<PackageMetadataBackfillService>().Single();
        await backfill.BackfillAsync(CancellationToken.None);

        Assert.Equal(expectedSize, (await FindPackageAsync()).Size);
    }

    private async Task<Package> FindPackageAsync()
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.Packages.AsNoTracking().SingleAsync(p => p.Id == "TestData");
    }

    private async Task UpdatePackageAsync(Action<Package> update)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        update(await context.Packages.SingleAsync(p => p.Id == "TestData"));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
