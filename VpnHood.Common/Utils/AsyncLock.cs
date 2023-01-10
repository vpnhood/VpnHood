using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Common.Utils;

public class AsyncLock
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SemaphoreSlims = new();

    private class Semaphore : ILockAsyncResult
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly string? _name;
        private bool _disposed;
        public bool Succeeded { get; }

        public Semaphore(SemaphoreSlim semaphoreSlim, bool succeeded, string? name)
        {
            _semaphoreSlim = semaphoreSlim;
            _name = name;
            Succeeded = succeeded;
        }

        public void Dispose()
        {
            if (_disposed || !Succeeded)
                return;

            _semaphoreSlim.Release();
            lock (SemaphoreSlims)
            {
                if (_semaphoreSlim.CurrentCount == 0 && _name != null)
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
        var succeeded = await _semaphoreSlim.WaitAsync(timeout, cancellationToken);
        return new Semaphore(_semaphoreSlim, succeeded, null);
    }

    public static async Task<ILockAsyncResult> LockAsync(string name)
    {
        SemaphoreSlim semaphoreSlim;
        lock (SemaphoreSlims)
            semaphoreSlim = SemaphoreSlims.GetOrAdd(name, new SemaphoreSlim(1, 1));

        await semaphoreSlim.WaitAsync();
        return new Semaphore(semaphoreSlim, true, name);
    }

    public static async Task<ILockAsyncResult> LockAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim semaphoreSlim;
        lock (SemaphoreSlims)
            semaphoreSlim = SemaphoreSlims.GetOrAdd(name, new SemaphoreSlim(1, 1));

        var succeeded = await semaphoreSlim.WaitAsync(timeout, cancellationToken);
        return new Semaphore(semaphoreSlim, succeeded, name);
    }
}