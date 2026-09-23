using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Quic;
using VpnHood.Core.Quic.Droid.Interop;
using VpnHood.Core.Toolkit.Extensions;
using static Microsoft.Quic.MsQuic;

namespace VpnHood.Core.Quic.Droid;

/// <summary>
/// Adapts a single MsQuic stream to a <see cref="Stream"/>. Inbound data delivered by msquic
/// RECEIVE callback is copied into a <see cref="Pipe"/> that <see cref="ReadAsync(Memory{byte},CancellationToken)"/>
/// drains; writes are issued via StreamSend and await it until SEND_COMPLETE (backpressure).
/// Pointer work is confined to <c>unsafe</c> members so the async members compile.
/// </summary>
internal sealed class AndroidQuicStream : Stream
{
    private unsafe QUIC_HANDLE* _stream;
    private GCHandle _gch;
    private readonly Pipe _recvPipe = new(new PipeOptions(useSynchronizationContext: false));
    private readonly ConcurrentDictionary<IntPtr, TaskCompletionSource> _pendingSends = new();
    private bool _disposed;

    private AndroidQuicStream() { }

    /// <summary>Opens a new client-initiated bidirectional stream on the given connection.</summary>
    public static unsafe AndroidQuicStream OpenOutbound(QUIC_HANDLE* connection)
    {
        var stream = new AndroidQuicStream();
        stream._gch = GCHandle.Alloc(stream);
        try {
            QUIC_HANDLE* handle;
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
        var stream = new AndroidQuicStream { _stream = handle };
        stream._gch = GCHandle.Alloc(stream);
        MsQuicApi.Table->SetCallbackHandler(handle,
            (delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_STREAM_EVENT*, int>)&StreamCallback,
            (void*)GCHandle.ToIntPtr(stream._gch));
        return stream;
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
        var result = await _recvPipe.Reader.ReadAsync(cancellationToken).Vhc();
        var seq = result.Buffer;
        if (seq.IsEmpty && result.IsCompleted) {
            _recvPipe.Reader.AdvanceTo(seq.End);
            return 0; // EOF
        }

        var n = (int)Math.Min(seq.Length, buffer.Length);
        seq.Slice(0, n).CopyTo(buffer.Span);
        _recvPipe.Reader.AdvanceTo(seq.GetPosition(n));
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
            return;

        var tcs = PostSend(buffer.Span);
        await using var reg = cancellationToken.Register(
            static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs);
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
        _pendingSends[(IntPtr)block] = tcs;
        var status = MsQuicApi.Table->StreamSend(_stream, qb, 1, QUIC_SEND_FLAGS.NONE, block);
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
                ref var recv = ref evt->RECEIVE;
                for (uint i = 0; i < recv.BufferCount; i++) {
                    var qb = recv.Buffers[i];
                    if (qb.Length == 0) continue;
                    var src = new ReadOnlySpan<byte>(qb.Buffer, (int)qb.Length);
                    var dst = _recvPipe.Writer.GetSpan((int)qb.Length);
                    src.CopyTo(dst);
                    _recvPipe.Writer.Advance((int)qb.Length);
                }
                _ = _recvPipe.Writer.FlushAsync(); // wake the reader (backpressure intentionally not applied here)
                break;

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

    private void FailPendingSends()
    {
        foreach (var key in _pendingSends.Keys)
            if (_pendingSends.TryRemove(key, out var tcs))
                tcs.TrySetException(new IOException("QUIC stream shut down."));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int StreamCallback(QUIC_HANDLE* stream, void* ctx, QUIC_STREAM_EVENT* evt)
    {
        var self = (AndroidQuicStream)GCHandle.FromIntPtr((IntPtr)ctx).Target!;
        try {
            return self.OnEvent(evt);
        }
        catch {
            return QUIC_STATUS_SUCCESS;
        }
    }

    protected override unsafe void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (_stream != null) {
            try { MsQuicApi.Table->StreamShutdown(_stream, QUIC_STREAM_SHUTDOWN_FLAGS.GRACEFUL, 0); } catch { /* ignore */ }
            MsQuicApi.Table->StreamClose(_stream); // blocks until callbacks drained
            _stream = null;
        }
        try { _recvPipe.Writer.Complete(); } catch { /* ignore */ }
        try { _recvPipe.Reader.Complete(); } catch { /* ignore */ }
        FailPendingSends();
        if (_gch.IsAllocated) _gch.Free();
        base.Dispose(disposing);
    }
}
