using System.Diagnostics;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.Channels.Streams;

// Only a negotiated TCP forwarding stream owns this state. No buffer is kept for its lifetime, and a new logical
// stream gets a fresh budget even when its underlying TLS connection is reused.
internal sealed class InitialStreamPadding
{
    private readonly WebSocketFrameWriter _writer;
    private const int MaximumLifetimeMilliseconds = 2000;
    private long? _started;
    private readonly CancellationTokenSource _stop = new();
    private int _remainingBytes = Random.Shared.Next(4096, 12289);
    private int _remainingWrites = Random.Shared.Next(2, 6);
    private readonly int _coverCount = Random.Shared.Next(1, 4);
    private Task? _coverTask;
    private readonly Lock _lifetimeLock = new();
    private bool _disposed;

    public InitialStreamPadding(WebSocketFrameWriter writer)
    {
        _writer = writer;
        CancellationToken = _stop.Token;
    }

    public CancellationToken CancellationToken { get; }
    public bool CanPadData => !CancellationToken.IsCancellationRequested && _remainingWrites > 0;

    // The frame writer holds its gate while cover can still share this budget and the transport.
    // This class owns padding policy and its lifetime; frame encoding and serialization stay in the writer.
    public async ValueTask<bool> TryWrite(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var paddingLength = TakePaddingLength(data.Length, cover: false);
        if (paddingLength == 0)
            return false;
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
        await _writer.WritePaddedData(data, paddingLength, writeCts.Token).Vhc();
        if (_coverTask == null) {
            _writer.StartCover();
            _coverTask = RunCover(CancellationToken);
        }
        return true;
    }

    // Counting the largest header keeps the entire wire overhead within 12 KiB per direction,
    // including the independently scheduled gap frames.
    public int TakePaddingLength(int dataLength, bool cover)
    {
        // The server enables padding before connecting to the destination. A slow connect must not
        // consume the window before its first session response is written.
        var started = _started ??= Stopwatch.GetTimestamp();
        if (CancellationToken.IsCancellationRequested || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= MaximumLifetimeMilliseconds ||
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

    // The window only limits starting frames; cancelling an in-flight cover write can break the stream.
    private async Task RunCover(CancellationToken cancellationToken)
    {
        try {
            for (var i = 0; i < _coverCount; i++) {
                var writeCount = _writer.WroteChunkCount;
                var delay = Random.Shared.Next(40, 351);
                await Task.Delay(delay, cancellationToken).Vhc();
                if (_writer.WroteChunkCount != writeCount)
                    continue; // Busy streams need no gap cover. Never make real data wait for a timer.
                if (!await _writer.TryWriteCover(this, cancellationToken).Vhc())
                    break;
            }
        }
        catch (OperationCanceledException) {
            // A logical close joins this task before reuse; a transport close needs no padding hook.
        }
        finally {
            _writer.FinishCover();
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

    // A quick close can race the graceful close's wait for cover; release cancellation state once.
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
