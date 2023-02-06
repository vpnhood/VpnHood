using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Common.Utils;

public class AsyncLock
{
    private readonly SemaphoreSlimEx _semaphoreSlimEx = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlimEx> SemaphoreSlims = new();

    public interface ILockAsyncResult : IDisposable
    {
        public bool Succeeded { get; }
    }

    private class SemaphoreSlimEx : SemaphoreSlim
    {
        public SemaphoreSlimEx(int initialCount, int maxCount)
            : base(initialCount, maxCount) { }

        public int ReferenceCount { get; set; }
    }

    private class SemaphoreLock : ILockAsyncResult
    {
        private readonly SemaphoreSlimEx _semaphoreSlimEx;
        private readonly string? _name;
        private bool _disposed;
        public bool Succeeded { get; }

        public SemaphoreLock(SemaphoreSlimEx semaphoreSlimEx, bool succeeded, string? name)
        {
            _semaphoreSlimEx = semaphoreSlimEx;
            _name = name;
            Succeeded = succeeded;
        }

        public void Dispose()
        {
            if (_disposed || !Succeeded)
                return;

            _semaphoreSlimEx.Release();
            lock (SemaphoreSlims)
            {
                _semaphoreSlimEx.ReferenceCount--;
                if (_semaphoreSlimEx.ReferenceCount == 0 && _name != null)
                    SemaphoreSlims.TryRemove(_name, out _);
            }
            _disposed = true;
        }
    }

    public Task<ILockAsyncResult> LockAsync()
    {
        return LockAsync(Timeout.InfiniteTimeSpan);
    }

    public async Task<ILockAsyncResult> LockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var succeeded = await _semaphoreSlimEx.WaitAsync(timeout, cancellationToken);
        return new SemaphoreLock(_semaphoreSlimEx, succeeded, null);
    }

    public static async Task<ILockAsyncResult> LockAsync(string name)
    {
        return await LockAsync(name, Timeout.InfiniteTimeSpan);
    }

    public static async Task<ILockAsyncResult> LockAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        SemaphoreSlimEx semaphoreSlim;
        lock (SemaphoreSlims)
        {
            semaphoreSlim = SemaphoreSlims.GetOrAdd(name, new SemaphoreSlimEx(1, 1));
            semaphoreSlim.ReferenceCount++;
        }

        var succeeded = await semaphoreSlim.WaitAsync(timeout, cancellationToken);
        return new SemaphoreLock(semaphoreSlim, succeeded, name);
    }
}