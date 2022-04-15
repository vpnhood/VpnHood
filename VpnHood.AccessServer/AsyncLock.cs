using System;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.AccessServer;

public class AsyncLock
{
    private class SemaphoreLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        public SemaphoreLock(SemaphoreSlim semaphoreSlim)
        {
            _semaphoreSlim = semaphoreSlim;
        }

        public void Dispose()
        {
            _semaphoreSlim.Release();
        }
    }

    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    public async Task<IDisposable> LockAsync()
    {
        var ret = new SemaphoreLock(_semaphoreSlim);
        await _semaphoreSlim.WaitAsync();
        return ret;
    }
}