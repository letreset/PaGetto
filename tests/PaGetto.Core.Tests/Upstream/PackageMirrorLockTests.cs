using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Upstream;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Upstream;

public class PackageMirrorLockTests
{
    public class AcquireAsync : FactsBase
    {
        [Fact]
        public async Task SameKeyWaitsForRelease()
        {
            var first = await Target.AcquireAsync(FeedId, "Package", Version, CancellationToken.None);

            var second = Target.AcquireAsync(FeedId, "PACKAGE", new NuGetVersion("1.0"), CancellationToken.None);
            await Task.Delay(50);
            Assert.False(second.IsCompleted);

            first.Dispose();
            using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task DifferentKeysDoNotWait()
        {
            // Many keys held at once: a fixed set of striped locks would make some of them collide.
            var held = new IDisposable[256];
            for (var i = 0; i < held.Length; i++)
            {
                held[i] = await Target
                    .AcquireAsync(Guid.NewGuid(), "Package", Version, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }

            using var otherPackage = await Target
                .AcquireAsync(FeedId, "Other", Version, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            foreach (var handle in held)
            {
                handle.Dispose();
            }
        }

        [Fact]
        public async Task CancelledWaitDoesNotBlockLaterRequests()
        {
            var first = await Target.AcquireAsync(FeedId, "Package", Version, CancellationToken.None);

            using var cts = new CancellationTokenSource();
            var cancelled = Target.AcquireAsync(FeedId, "Package", Version, cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            first.Dispose();
            using var acquired = await Target
                .AcquireAsync(FeedId, "Package", Version, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    public class FactsBase
    {
        protected readonly Guid FeedId = Guid.NewGuid();
        protected readonly NuGetVersion Version = new NuGetVersion("1.0.0");
        protected readonly PackageMirrorLock Target = new PackageMirrorLock();
    }
}
