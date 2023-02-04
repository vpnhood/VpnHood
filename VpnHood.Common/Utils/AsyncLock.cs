using AsyncKeyedLock;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Common.Utils;

public class AsyncLock
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private static readonly AsyncKeyedLocker<string> AsyncKeyedLocker = new(o =>
    {
        o.PoolSize = 20;
        o.PoolInitialFill = 1;
    });

    private class Semaphore : ILockAsyncResult
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        private bool _disposed;
        public bool Succeeded { get; }

        public Semaphore(SemaphoreSlim semaphoreSlim, bool succeeded)
        {
            _semaphoreSlim = semaphoreSlim;
            Succeeded = succeeded;
        }

        public void Dispose()
        {
            if (_disposed || !Succeeded)
                return;

            _semaphoreSlim.Release();
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
        return new Semaphore(_semaphoreSlim, succeeded);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IDisposable> LockAsync(string name)
    {
        return AsyncKeyedLocker.LockAsync(name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<AsyncKeyedLockTimeoutReleaser<string>> LockAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return AsyncKeyedLocker.LockAsync(name, timeout, cancellationToken);
    }
}