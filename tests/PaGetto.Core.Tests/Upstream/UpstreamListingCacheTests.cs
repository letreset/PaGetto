using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Upstream;
using Xunit;

namespace PaGetto.Core.Tests.Upstream;

public class UpstreamListingCacheTests
{
    public class GetOrListAsync : FactsBase
    {
        [Fact]
        public async Task ConcurrentMissesCallListOnce()
        {
            var calls = Enumerable.Range(0, 5)
                .Select(_ => Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None))
                .ToList();

            Upstream.SetResult(["1.0.0"]);
            var results = await Task.WhenAll(calls);

            Assert.Equal(1, ListCalls);
            Assert.All(results, r => Assert.Equal(new[] { "1.0.0" }, r));
        }

        [Fact]
        public async Task ConcurrentMissesShareAnEmptyResultWithoutCachingIt()
        {
            var first = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);
            var second = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);

            Upstream.SetResult([]);
            await Task.WhenAll(first, second);
            Assert.Equal(1, ListCalls);

            Upstream = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var third = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);
            Upstream.SetResult([]);
            await third;

            Assert.Equal(2, ListCalls);
        }

        [Fact]
        public async Task CancelledWaiterDoesNotAffectOthers()
        {
            using var cancellation = new CancellationTokenSource();
            var cancelled = Target.GetOrListAsync(Key, Duration, ListAsync, cancellation.Token);
            var other = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            Upstream.SetResult(["1.0.0"]);
            Assert.Equal(new[] { "1.0.0" }, await other);

            // The shared call completed and was cached even though one waiter left.
            await Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);
            Assert.Equal(1, ListCalls);
        }

        [Fact]
        public async Task SharedCallDoesNotUseTheCallersToken()
        {
            using var cancellation = new CancellationTokenSource();
            var call = Target.GetOrListAsync(Key, Duration, ListAsync, cancellation.Token);

            Upstream.SetResult(["1.0.0"]);
            await call;

            Assert.False(ListToken.CanBeCanceled);
        }

        [Fact]
        public async Task FailedCallReachesEveryWaiterAndIsNotCached()
        {
            var first = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);
            var second = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);

            Upstream.SetException(new InvalidOperationException("upstream failed"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
            await Assert.ThrowsAsync<InvalidOperationException>(() => second);

            Upstream = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retry = Target.GetOrListAsync(Key, Duration, ListAsync, CancellationToken.None);
            Upstream.SetResult(["1.0.0"]);

            Assert.Equal(new[] { "1.0.0" }, await retry);
            Assert.Equal(2, ListCalls);
        }

        [Fact]
        public async Task DifferentKeysDoNotShareACall()
        {
            var first = Target.GetOrListAsync("a", Duration, ListAsync, CancellationToken.None);
            var second = Target.GetOrListAsync("b", Duration, ListAsync, CancellationToken.None);

            Upstream.SetResult(["1.0.0"]);
            await Task.WhenAll(first, second);

            Assert.Equal(2, ListCalls);
        }
    }

    public class FactsBase : IDisposable
    {
        protected const string Key = "key";
        protected static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);

        protected readonly UpstreamListingCache Target = new();

        // Every list call waits on the current source, so tests control when the upstream answers.
        protected TaskCompletionSource<IReadOnlyList<string>> Upstream = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected int ListCalls;
        protected CancellationToken ListToken;

        protected Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ListCalls);
            ListToken = cancellationToken;
            return Upstream.Task;
        }

        public void Dispose()
        {
            Target.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
