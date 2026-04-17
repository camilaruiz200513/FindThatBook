using FindThatBook.Infrastructure.OpenLibrary;
using FluentAssertions;

namespace FindThatBook.Tests.Infrastructure.OpenLibrary;

public class CatalogCacheCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_serializes_concurrent_callers_for_the_same_key()
    {
        var coordinator = new CatalogCacheCoordinator();
        var inFlight = 0;
        var maxSeen = 0;
        var lockObj = new object();

        async Task Work()
        {
            using var _ = await coordinator.AcquireAsync("shared-key", CancellationToken.None);
            lock (lockObj)
            {
                inFlight++;
                if (inFlight > maxSeen) maxSeen = inFlight;
            }
            await Task.Delay(20);
            lock (lockObj)
            {
                inFlight--;
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Work()));

        // With per-key serialization, only one worker should ever be inside
        // the critical section at a time.
        maxSeen.Should().Be(1);
    }

    [Fact]
    public async Task AcquireAsync_allows_parallelism_across_different_keys()
    {
        var coordinator = new CatalogCacheCoordinator();
        var inFlight = 0;
        var maxSeen = 0;
        var lockObj = new object();

        async Task Work(string key)
        {
            using var _ = await coordinator.AcquireAsync(key, CancellationToken.None);
            lock (lockObj)
            {
                inFlight++;
                if (inFlight > maxSeen) maxSeen = inFlight;
            }
            await Task.Delay(20);
            lock (lockObj)
            {
                inFlight--;
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Work($"key-{i}")));

        // Different keys should not block each other.
        maxSeen.Should().BeGreaterThan(1);
    }
}
