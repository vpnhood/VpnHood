using System.Collections.Concurrent;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides a lock an async method can wait for, held until the returned result is disposed: an instance's own, or a
/// process-wide one by name.
/// </summary>
public sealed class AsyncLock
{
    private readonly SemaphoreSlimEx _semaphoreSlimEx = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlimEx> SemaphoreSlims = new();

    /// <summary>
    /// Represents a wait for the lock: dispose every one, a timed-out one too, as it releases the lock it took, once, and
    /// ends a named lock's bookkeeping.
    /// </summary>
    public interface ILockAsyncResult : IDisposable
    {
        /// <summary>Gets a value indicating whether the lock was taken; <see langword="false"/> after a timeout.</summary>
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
            if (Interlocked.Exchange(ref _disposed, true))
                return;

            if (Succeeded)
                semaphoreSlimEx.Release();

            // an instance's lock is no named lock: it has no count to keep, and no reason to wait for the lock that
            // every named lock of the process shares
            if (name == null)
                return;

            lock (SemaphoreSlims) {
                semaphoreSlimEx.ReferenceCount--;
                if (semaphoreSlimEx.ReferenceCount == 0)
                    SemaphoreSlims.TryRemove(name, out _);
            }
        }
    }

    /// <summary>Gets a value indicating whether the instance's lock is held.</summary>
    public bool IsLocked => _semaphoreSlimEx.CurrentCount == 0;

    /// <summary>Waits for the instance's lock.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result holds the lock until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<ILockAsyncResult> LockAsync(CancellationToken cancellationToken)
    {
        await _semaphoreSlimEx.WaitAsync(cancellationToken).Vhc();
        return new SemaphoreLock(_semaphoreSlimEx, true, null);
    }

    /// <summary>Waits for the instance's lock, at most a timeout.</summary>
    /// <param name="timeout">How long to wait, or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result tells whether the lock was taken, and holds it until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, other than infinite, or too long.</exception>
    public async Task<ILockAsyncResult> LockAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var succeeded = await _semaphoreSlimEx.WaitAsync(timeout, cancellationToken).Vhc();
        return new SemaphoreLock(_semaphoreSlimEx, succeeded, null);
    }

    /// <summary>Waits for the process-wide lock of a name.</summary>
    /// <param name="name">The lock's name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result holds the lock until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static Task<ILockAsyncResult> LockAsync(string name, CancellationToken cancellationToken)
    {
        return LockAsync(name, Timeout.InfiniteTimeSpan, cancellationToken);
    }


    /// <summary>
    /// Waits for the process-wide lock of a name, at most a timeout. The name's lock lasts until no wait for it remains and every
    /// result taken for it, a timed-out one included, is disposed.
    /// </summary>
    /// <param name="name">The lock's name.</param>
    /// <param name="timeout">How long to wait, or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result tells whether the lock was taken, and holds it until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, other than infinite, or too long.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static async Task<ILockAsyncResult> LockAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SemaphoreSlimEx semaphoreSlim;
        lock (SemaphoreSlims) {
            semaphoreSlim = SemaphoreSlims.GetOrAdd(name, _ => new SemaphoreSlimEx(1, 1));
            semaphoreSlim.ReferenceCount++;
        }

        try {
            var succeeded = await semaphoreSlim.WaitAsync(timeout, cancellationToken).Vhc();
            return new SemaphoreLock(semaphoreSlim, succeeded, name);
        }
        catch {
            lock (SemaphoreSlims) {
                semaphoreSlim.ReferenceCount--;
                if (semaphoreSlim.ReferenceCount == 0)
                    SemaphoreSlims.TryRemove(name, out _);
            }

            throw;
        }
    }
}