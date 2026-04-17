using System.Collections.Concurrent;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// Per-key locking for the catalog cache. When multiple requests miss the same
/// cache key simultaneously, only one calls Open Library; the others wait and
/// read the populated entry. Without this the standard pattern of
/// "check cache, fetch on miss, populate" has a thundering-herd problem under
/// load. A lightweight alternative to pulling in LazyCache / AsyncLazy.
/// </summary>
public sealed class CatalogCacheCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
