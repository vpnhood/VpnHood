using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Quic;
using VpnHood.Net.Quic.Android.Interop;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using static Microsoft.Quic.MsQuic;

namespace VpnHood.Net.Quic.Android;

/// <summary>
/// Adapts a single MsQuic stream to a <see cref="Stream"/>. Inbound data delivered by msquic
/// RECEIVE callback is copied into a <see cref="Pipe"/> that <see cref="ReadAsync(Memory{byte},CancellationToken)"/>
/// drains, up to <see cref="ReceiveBufferLimit"/>: past it the stream's receive events stop until the reader has made
/// room, so the stream's flow control holds the sender. Writes are issued via StreamSend and await it until
/// SEND_COMPLETE (backpressure). Pointer work is confined to <c>unsafe</c> members so the async members compile.
/// </summary>
internal sealed class AndroidQuicStream : Stream
{
    // The most received bytes held for the reader, as much as System.Net.Quic holds. Beyond it MsQuic keeps what
    // arrives, within the stream's flow-control window, which then stops the peer.
    private const int ReceiveBufferLimit = 64 * 1024;

    // Held around every use of the stream's handle outside its callbacks: a send or a resumed receive never uses it
    // while the disposal closes it, or after. The callbacks the close waits for never take it.
    private readonly Lock _handleLock = new();
    private unsafe QUIC_HANDLE* _stream;
    private GCHandle _gch;

    // Never pauses its writer: the limit above bounds it, so a flush always completes at once
    private readonly Pipe _recvPipe = new(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0,
        useSynchronizationContext: false));
    private readonly ConcurrentDictionary<IntPtr, TaskCompletionSource> _pendingSends = new();
    private readonly TaskCompletionSource? _inboundShutdown;
    private long _receiveBufferedBytes; // in the pipe, not read yet
    private int _receivePaused; // 1 while MsQuic holds the receive events back, as a receive took less than it was given
    private bool _disposed;

    // room to take up the receive events again: at half the limit, so a slow reader does not resume them per read
    private bool HasReceiveRoom => Interlocked.Read(ref _receiveBufferedBytes) <= ReceiveBufferLimit / 2;

    private AndroidQuicStream(bool isInbound = false)
    {
        if (isInbound)
            _inboundShutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Opens a new client-initiated bidirectional stream on the given connection.</summary>
    public static unsafe AndroidQuicStream OpenOutbound(QUIC_HANDLE* connection)
    {
        var stream = new AndroidQuicStream();
        stream._gch = GCHandle.Alloc(stream);
        try {
            QUIC_HANDLE* handle = null;
            ThrowIfFailure(MsQuicApi.Table->StreamOpen(connection, QUIC_STREAM_OPEN_FLAGS.NONE,
                &StreamCallback, (void*)GCHandle.ToIntPtr(stream._gch), &handle));
            stream._stream = handle;
            ThrowIfFailure(MsQuicApi.Table->StreamStart(handle, QUIC_STREAM_START_FLAGS.IMMEDIATE));
            return stream;
        }
        catch {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Adopts a peer-initiated stream handle (sets our callback on it).</summary>
    public static unsafe AndroidQuicStream FromInbound(QUIC_HANDLE* handle)
    {
        var stream = new AndroidQuicStream(isInbound: true) { _stream = handle };
        stream._gch = GCHandle.Alloc(stream);
        MsQuicApi.Table->SetCallbackHandler(handle,
            (delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_STREAM_EVENT*, int>)&StreamCallback,
            (void*)GCHandle.ToIntPtr(stream._gch));
        return stream;
    }

    // Only the inbound queue calls this, before ownership passes to an accepting caller. Its atomic
    // disposal guard also prevents a second cleanup from closing the same native handle.
    public unsafe void Discard()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        try {
            if (_inboundShutdown is { Task.IsCompleted: false }) {
                // MsQuic requires both abort flags with IMMEDIATE, even for a receive-only stream.
                // This does not wait for the peer, but SHUTDOWN_COMPLETE must precede StreamClose.
                ThrowIfFailure(MsQuicApi.Table->StreamShutdown(_stream,
                    QUIC_STREAM_SHUTDOWN_FLAGS.ABORT_SEND | QUIC_STREAM_SHUTDOWN_FLAGS.ABORT_RECEIVE |
                    QUIC_STREAM_SHUTDOWN_FLAGS.IMMEDIATE, 0));
            }
        }
        catch (Exception ex) {
            // The queue is closed only during connection shutdown. Its close will complete the
            // stream shutdown as well if MsQuic could not allocate this separate abort operation.
            VhLogger.Instance.LogDebug(ex, "Could not abort an unaccepted QUIC stream.");
        }

        // Always leave the native callback before closing. The shutdown event may already have
        // arrived, so relying on an incomplete await alone would not keep StreamClose off that thread.
        _ = Task.Run(CloseDiscardedStream);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = await _recvPipe.Reader.ReadAsync(cancellationToken).Vhc();
        var seq = result.Buffer;
        if (seq.IsEmpty && result.IsCompleted) {
            _recvPipe.Reader.AdvanceTo(seq.End);
            return 0; // EOF
        }

        var n = (int)Math.Min(seq.Length, buffer.Length);
        seq.Slice(0, n).CopyTo(buffer.Span);
        _recvPipe.Reader.AdvanceTo(seq.GetPosition(n));

        // room again for what MsQuic holds back: its receive events go on
        Interlocked.Add(ref _receiveBufferedBytes, -n);
        if (HasReceiveRoom && Interlocked.CompareExchange(ref _receivePaused, 0, 1) == 1)
            ResumeReceive();

        return n;
    }

    private unsafe void ResumeReceive()
    {
        int status;
        lock (_handleLock) {
            if (_stream == null)
                return; // closed meanwhile: nothing more to receive

            status = MsQuicApi.Table->StreamReceiveSetEnabled(_stream, 1);
        }

        // a stream whose receives stay off would stall its reader for good: the reader gets the error instead
        if (StatusFailed(status))
            _recvPipe.Writer.Complete(new IOException($"Could not resume the QUIC stream's receives. status=0x{(uint)status:x}"));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return;

        var tcs = PostSend(buffer.Span);
        await using var reg = cancellationToken.Register(static state => {
            if (state is TaskCompletionSource completion)
                completion.TrySetCanceled();
        }, tcs);
        await tcs.Task.Vhc(); // completed on SEND_COMPLETE
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    // One native block holds [QUIC_BUFFER][data] so both survive until SEND_COMPLETE.
    private unsafe TaskCompletionSource PostSend(ReadOnlySpan<byte> data)
    {
        var len = data.Length;
        var block = (byte*)NativeMemory.Alloc((nuint)(sizeof(QUIC_BUFFER) + len));
        var qb = (QUIC_BUFFER*)block;
        var dataPtr = block + sizeof(QUIC_BUFFER);
        data.CopyTo(new Span<byte>(dataPtr, len));
        qb->Length = (uint)len;
        qb->Buffer = dataPtr;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int status;
        lock (_handleLock) {
            // not on a handle the disposal has closed, or is closing
            if (_disposed || _stream == null) {
                NativeMemory.Free(block);
                throw new ObjectDisposedException(nameof(AndroidQuicStream));
            }

            _pendingSends[(IntPtr)block] = tcs;
            status = MsQuicApi.Table->StreamSend(_stream, qb, 1, QUIC_SEND_FLAGS.NONE, block);
        }

        if (StatusFailed(status)) {
            _pendingSends.TryRemove((IntPtr)block, out _);
            NativeMemory.Free(block);
            ThrowIfFailure(status);
        }
        return tcs;
    }

    private unsafe int OnEvent(QUIC_STREAM_EVENT* evt)
    {
        switch (evt->Type) {
            case QUIC_STREAM_EVENT_TYPE.RECEIVE:
                return OnReceive(evt);

            case QUIC_STREAM_EVENT_TYPE.SEND_COMPLETE:
                var ctx = (IntPtr)evt->SEND_COMPLETE.ClientContext;
                if (_pendingSends.TryRemove(ctx, out var tcs)) {
                    if (evt->SEND_COMPLETE.Canceled != 0)
                        tcs.TrySetException(new IOException("QUIC send was canceled."));
                    else
                        tcs.TrySetResult();
                }
                if (ctx != IntPtr.Zero)
                    NativeMemory.Free((void*)ctx);
                break;

            case QUIC_STREAM_EVENT_TYPE.PEER_SEND_SHUTDOWN:
                _recvPipe.Writer.Complete(); // EOF for the reader
                break;

            case QUIC_STREAM_EVENT_TYPE.PEER_SEND_ABORTED:
                _recvPipe.Writer.Complete(new IOException(
                    $"QUIC peer aborted send. error=0x{evt->PEER_SEND_ABORTED.ErrorCode:x}"));
                break;

            case QUIC_STREAM_EVENT_TYPE.SHUTDOWN_COMPLETE:
                _recvPipe.Writer.Complete();
                FailPendingSends();
                break;
        }
        return QUIC_STATUS_SUCCESS;
    }

    // Takes what fits under the limit and tells MsQuic how much that was. Less than it was given stops the stream's
    // receive events until the reader has made room (ReadAsync), as System.Net.Quic does it: what MsQuic then keeps
    // fills the stream's flow-control window, and the peer stops sending.
    private unsafe int OnReceive(QUIC_STREAM_EVENT* evt)
    {
        ref var receive = ref evt->RECEIVE;
        var room = ReceiveBufferLimit - Interlocked.Read(ref _receiveBufferedBytes);
        long taken = 0;
        for (uint i = 0; i < receive.BufferCount && taken < room; i++) {
            var buffer = receive.Buffers[i];
            var length = (int)Math.Min(buffer.Length, room - taken);
            if (length == 0)
                continue;

            new ReadOnlySpan<byte>(buffer.Buffer, length).CopyTo(_recvPipe.Writer.GetSpan(length));
            _recvPipe.Writer.Advance(length);
            taken += length;
        }

        if (taken > 0) {
            Interlocked.Add(ref _receiveBufferedBytes, taken);
            _ = _recvPipe.Writer.FlushAsync(); // wakes the reader; complete at once, as the pipe never pauses its writer
        }

        if ((ulong)taken == receive.TotalBufferLength)
            return QUIC_STATUS_SUCCESS;

        // The rest stays with MsQuic. The reader takes the events up again once it has made room; where it made that
        // room just now, before the mark below, it found nothing to take up, and they go on from here.
        receive.TotalBufferLength = (ulong)taken;
        Volatile.Write(ref _receivePaused, 1);
        return HasReceiveRoom && Interlocked.CompareExchange(ref _receivePaused, 0, 1) == 1
            ? QUIC_STATUS_CONTINUE
            : QUIC_STATUS_SUCCESS;
    }

    private void FailPendingSends()
    {
        foreach (var key in _pendingSends.Keys)
            if (_pendingSends.TryRemove(key, out var tcs))
                tcs.TrySetException(new IOException("QUIC stream shut down."));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int StreamCallback(QUIC_HANDLE* stream, void* ctx, QUIC_STREAM_EVENT* evt)
    {
        if (GCHandle.FromIntPtr((IntPtr)ctx).Target is not AndroidQuicStream self)
            return QUIC_STATUS_SUCCESS;
        try {
            return self.OnEvent(evt);
        }
        catch {
            return QUIC_STATUS_SUCCESS;
        }
        finally {
            if (evt->Type == QUIC_STREAM_EVENT_TYPE.SHUTDOWN_COMPLETE)
                self._inboundShutdown?.TrySetResult();
        }
    }

    private async Task CloseDiscardedStream()
    {
        // Cleanup outlives setup cancellation. After IMMEDIATE, this wait is for native shutdown,
        // not a network acknowledgement; retaining the GCHandle keeps the final callback valid.
        if (_inboundShutdown is { } shutdown)
            await shutdown.Task.Vhc();
        try {
            CloseResources();
            base.Dispose(true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not close an unaccepted QUIC stream.");
        }
    }

    private unsafe void CloseResources()
    {
        lock (_handleLock) {
            if (_stream != null) {
                MsQuicApi.Table->StreamClose(_stream); // blocks until callbacks drained
                _stream = null;
            }
        }

        try { _recvPipe.Writer.Complete(); } catch { /* ignore */ }
        try { _recvPipe.Reader.Complete(); } catch { /* ignore */ }
        FailPendingSends();
        if (_gch.IsAllocated) _gch.Free();
    }

    protected override unsafe void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        lock (_handleLock) {
            if (_stream != null) {
                try { MsQuicApi.Table->StreamShutdown(_stream, QUIC_STREAM_SHUTDOWN_FLAGS.GRACEFUL, 0); } catch { /* ignore */ }
            }
        }

        CloseResources();
        base.Dispose(disposing);
    }
}
