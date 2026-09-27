using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace PaGetto.Core.Upstream;

/// <summary>
/// Serializes mirroring of a single <c>(feed, id, version)</c> so that concurrent requests for the
/// same not-yet-cached package don't all download, store and index it in parallel.
/// </summary>
/// <remarks>
/// Register as a singleton. Each key has its own semaphore, so different packages and feeds never
/// wait on each other, even when one feed mirrors another feed of the same instance. A semaphore is
/// removed once no request holds or waits for it, so memory is bounded by the in-flight mirrors.
/// </remarks>
public class PackageMirrorLock
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(
        Guid feedId,
        string id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        var key = $"{feedId}/{id.ToLowerInvariant()}/{version.ToNormalizedString().ToLowerInvariant()}";

        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            RemoveReference(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        RemoveReference(key, entry);
    }

    private void RemoveReference(string key, Entry entry)
    {
        lock (_entries)
        {
            entry.References--;
            if (entry.References == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly PackageMirrorLock _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(PackageMirrorLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, _entry);
            }
        }
    }
}
