using System.Collections.Concurrent;

namespace VpnHood.Common.Utils;

public sealed class AsyncLock
{
    private readonly SemaphoreSlimEx _semaphoreSlimEx = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlimEx> SemaphoreSlims = new();

    public interface ILockAsyncResult : IDisposable
    {
        public bool Succeeded { get; }
    }

    private class SemaphoreSlimEx(int initialCount, int maxCount)
        : SemaphoreSlim(initialCount, maxCount)
    {
        public int ReferenceCount { get; set; }
    }

    private class SemaphoreLock(SemaphoreSlimEx semaphoreSlimEx, bool succeeded, string? name)
        : ILockAsyncResult
    {
        private bool _disposed;
        public bool Succeeded { get; } = succeeded;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Succeeded)
                semaphoreSlimEx.Release();

            lock (SemaphoreSlims)
            {
                semaphoreSlimEx.ReferenceCount--;
                if (semaphoreSlimEx.ReferenceCount == 0 && name != null)
                    SemaphoreSlims.TryRemove(name, out _);
            }
        }
    }

    public async Task<ILockAsyncResult> LockAsync(CancellationToken cancellationToken = default)
    {
        await _semaphoreSlimEx.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLock(_semaphoreSlimEx, true, null);
    }

    public async Task<ILockAsyncResult> LockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var succeeded = await _semaphoreSlimEx.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return new SemaphoreLock(_semaphoreSlimEx, succeeded, null);
    }

    public static Task<ILockAsyncResult> LockAsync(string name)
    {
        return LockAsync(name, Timeout.InfiniteTimeSpan);
    }

    public static async Task<ILockAsyncResult> LockAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        SemaphoreSlimEx semaphoreSlim;
        lock (SemaphoreSlims)
        {
            semaphoreSlim = SemaphoreSlims.GetOrAdd(name, _ => new SemaphoreSlimEx(1, 1));
            semaphoreSlim.ReferenceCount++;
        }

        try
        {
            var succeeded = await semaphoreSlim.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new SemaphoreLock(semaphoreSlim, succeeded, name);
        }
        catch
        {
            semaphoreSlim.ReferenceCount--;
            throw;
        }
    }
}