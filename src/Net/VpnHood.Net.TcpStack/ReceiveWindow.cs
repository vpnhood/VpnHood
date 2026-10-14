using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace VpnHood.Net.TcpStack;

/// <summary>
/// The peer's bytes waiting for the app, and the window advertised for them: the pipe the stream reads, its unread count,
/// and the 16-bit window - ReceiveWindowSize minus the unread bytes - which closes as the pipe fills and reopens as the app
/// drains. The connection bounds each write by <see cref="Available"/>, which makes ReceiveWindowSize the cap on the
/// per-connection receive memory on every platform. Used under the connection's lock; <see cref="Reader"/> and
/// <see cref="Unread"/> are used without it. Not thread-safe on its own.
/// </summary>
internal sealed class ReceiveWindow(LocalTcpStackOptions options, PipeOptions pipeOptions, ushort mss)
{
    // PipeOptions is built once per stack and shared by its connections; each connection owns its pipe.
    private readonly Pipe _pipe = new(pipeOptions);
    private readonly ushort _size = (ushort)options.ReceiveWindowSize;
    // The update thresholds, scaled to the window so they fire for a small one too. A window update goes out once the
    // window has opened by a quarter (never less than one MSS) since it was last advertised - a steady upload never closes
    // the window, so this is what keeps the peer's window sliding - or once a closed window has reopened to the floor.
    // On the 64 KB window: 16383 and 4096.
    private readonly int _updateThreshold = Math.Max(mss, options.ReceiveWindowSize / 4);
    private readonly int _reopenFloor = Math.Min(4096, options.ReceiveWindowSize);
    private int _unread; // an int, so the lock-free read cannot tear on 32-bit
    private bool _closed; // the pipe filled: the drain that reaches the floor re-advertises
    private bool _completed;

    /// <summary>What the stream reads.</summary>
    public PipeReader Reader => _pipe.Reader;

    /// <summary>The unread bytes, for the stack's diagnostic total.</summary>
    public int Unread => Volatile.Read(ref _unread);

    /// <summary>The window last put on the wire: the acceptability tests measure incoming segments against it, and
    /// <see cref="Consume"/> measures the reopening from it.</summary>
    public ushort LastAdvertised { get; private set; } = (ushort)options.ReceiveWindowSize;

    /// <summary>The window to advertise now: the size minus the unread bytes.</summary>
    public ushort Available {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // per segment in and out; Mono inlines nothing this size on its own
        get => (ushort)Math.Max(_size - _unread, 0);
    }

    /// <summary>Takes the window for a segment going out, recording it.</summary>
    public ushort Advertise() => LastAdvertised = Available;

    /// <summary>Appends in-order bytes for the app, at most <see cref="Available"/> of them: that holds the pipe below its
    /// pause threshold (LocalTcpStackOptions.CreatePipeOptions), so the flush completes synchronously and its ValueTask
    /// is dropped, never a pending one. A completed window drops them; more than Available throws.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_completed) return;
        var span = _pipe.Writer.GetSpan(data.Length);
        data.CopyTo(span);
        _pipe.Writer.Advance(data.Length);
        _unread += data.Length;
        if (_unread >= _size) {
            if (_unread > _size)
                throw new InvalidOperationException($"The receive window holds {_unread} unread bytes for a size of {_size}.");
            _closed = true;
        }
        _ = _pipe.Writer.FlushAsync();
    }

    /// <summary>The app read count bytes. Returns whether a window update is due: a closed window reopened to the floor, or
    /// the window opened by the threshold since it was last advertised.</summary>
    public bool Consume(long count)
    {
        _unread -= (int)Math.Min(_unread, count);
        var current = Available;
        var reopened = current >= _reopenFloor;
        var due = (_closed && reopened) || current - LastAdvertised >= _updateThreshold;
        if (reopened)
            _closed = false;
        return due;
    }

    /// <summary>Zeroes the unread count; the bytes stay in the pipe. Returns the count.</summary>
    public int ClearUnread()
    {
        var unread = _unread;
        _unread = 0;
        return unread;
    }

    /// <summary>Completes the writer: the stream's reads end after the bytes already in the pipe.</summary>
    public void Complete()
    {
        if (_completed) return;
        _completed = true;
        try { _pipe.Writer.Complete(); } catch { /* already completed */ }
    }
}
