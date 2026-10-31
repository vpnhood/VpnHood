using System.Diagnostics;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.Channels.Streams;

// Only a negotiated TCP proxy stream owns this state. No buffer is kept for its lifetime, and a new logical
// stream gets a fresh budget even when its underlying TLS connection is reused.
internal sealed class InitialStreamPadding
{
    private const int MaximumLifetimeMilliseconds = 2000;
    private long? _started;
    private readonly CancellationTokenSource _stop = new();
    private int _remainingBytes = Random.Shared.Next(4096, 12289);
    private int _remainingWrites = Random.Shared.Next(2, 6);
    private readonly int _coverCount = Random.Shared.Next(1, 4);
    private Task? _coverTask;
    private readonly Lock _lifetimeLock = new();
    private bool _disposed;

    public InitialStreamPadding()
    {
        CancellationToken = _stop.Token;
    }

    public CancellationToken CancellationToken { get; }
    public bool CanPadData => _remainingWrites > 0;

    // Called under the stream's write gate. Counting the largest header keeps the entire wire overhead
    // within 12 KiB per direction, including the independently scheduled gap frames.
    public int TakePaddingLength(int dataLength, bool cover)
    {
        // The server enables padding before connecting to the destination. A slow connect must not
        // consume the window before its first session response is written.
        var started = _started ??= Stopwatch.GetTimestamp();
        if (_stop.IsCancellationRequested || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= MaximumLifetimeMilliseconds ||
            _remainingBytes < 142) {
            _remainingWrites = 0;
            return 0;
        }

        if (!cover) {
            if (_remainingWrites-- <= 0 || dataLength > 12 * 1024)
                return 0;
        }

        var maximum = Math.Min(_remainingBytes - 14, cover ? 1400 : 3072);
        var length = Random.Shared.Next(128, maximum + 1);
        _remainingBytes -= length + 14;
        return length;
    }

    public void StartCover(Func<InitialStreamPadding, CancellationToken, Task> writeCover, Func<int> getWriteCount)
    {
        // The first real write leaves immediately; only the optional dummy frames wait on timers.
        _coverTask ??= RunCover(writeCover, getWriteCount, CancellationToken);
    }

    private async Task RunCover(Func<InitialStreamPadding, CancellationToken, Task> writeCover,
        Func<int> getWriteCount, CancellationToken cancellationToken)
    {
        try {
            for (var i = 0; i < _coverCount; i++) {
                var writeCount = getWriteCount();
                var delay = Random.Shared.Next(40, 351);
                await Task.Delay(delay, cancellationToken).Vhc();
                if (getWriteCount() != writeCount)
                    continue; // Busy streams need no gap cover. Never make real data wait for a timer.
                await writeCover(this, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            // The stream's close cancels a timer or a cover write, then waits for this task before reuse.
        }
    }

    public void Stop()
    {
        lock (_lifetimeLock) {
            if (!_disposed)
                _stop.TryCancel();
        }
    }

    public Task Completion => _coverTask ?? Task.CompletedTask;

    // Only the graceful owner releases the source after joining the cover task. The quick close merely
    // cancels it; the source's transport owner closes the socket, as it does for an ordinary data write.
    public void Dispose()
    {
        lock (_lifetimeLock) {
            if (_disposed)
                return;
            _disposed = true;
            _stop.TryCancel();
            _stop.Dispose();
        }
    }
}
