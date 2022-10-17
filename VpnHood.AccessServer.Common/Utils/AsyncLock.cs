using System.Collections.Concurrent;

namespace VpnHood.AccessServer.Utils;

public class AsyncLock
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SemaphoreSlims = new();

    private class SemaphoreLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly string? _name;
        private bool _disposed;

        public SemaphoreLock(SemaphoreSlim semaphoreSlim, string? name = null)
        {
            _semaphoreSlim = semaphoreSlim;
            _name = name;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _semaphoreSlim.Release();
            if (_semaphoreSlim.CurrentCount == 0 && _name != null)
                SemaphoreSlims.TryRemove(_name, out _);
            _disposed = true;
        }
    }

    public Task<IDisposable> LockAsync()
    {
        return LockAsync(Timeout.InfiniteTimeSpan);
    }

    public async Task<IDisposable> LockAsync(TimeSpan timeout)
    {
        var ret = new SemaphoreLock(_semaphoreSlim);
        await _semaphoreSlim.WaitAsync(timeout);
        return ret;
    }

    public static async Task<IDisposable> LockAsync(string name)
    {
        var semaphoreSlim = SemaphoreSlims.GetOrAdd(name, new SemaphoreSlim(1, 1));
        var ret = new SemaphoreLock(semaphoreSlim, name);
        await semaphoreSlim.WaitAsync();
        return ret;
    }

}