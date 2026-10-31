using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Core.Common.Configuration;

// ReSharper disable InconsistentlySynchronizedField

namespace VpnHood.Core.Tunneling.Channels.Streams;

public class WebSocketStream : ChunkStream, IPreservedChunkStream
{
    private readonly bool _isServer;
    private readonly bool _useStandardMasking;
    // A read can answer a ping while the proxy writes. Cover frames use the same gate, so no frame can
    // split another write. SemaphoreSlim's uncontended path avoids allocating a lock scope per packet.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private InitialStreamPadding? _initialPadding;
    private bool _paddingActive;
    private ReadOnlyMemory<byte> _readMask;
    private int _readMaskOffset;
    private bool? _fragmentedBinary;
    private const int ChunkHeaderLength = 14; // maximum websocket frame header length with inline padding length
    private int _remainingChunkBytes;
    private bool _finished;
    private Exception? _exception;
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;
    private readonly Memory<byte> _readChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private readonly Memory<byte> _writeChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private Task? _closeStreamTask;
    private readonly Lock _closeStreamLock = new();
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;
    public override bool CanReuse => !_isConnectionClosed && _exception == null;
    public int PreserveWriteBufferLength => ChunkHeaderLength;
    public bool UsesStandardMasking => _useStandardMasking;

    // the status the peer's close frame carried, if it carried one
    public ushort? CloseStatus { get; private set; }

    public WebSocketStream(Stream sourceStream, string streamId, bool useBuffer, bool isServer,
        bool useStandardMasking = false)
        : base(useBuffer
            ? new ReadBufferedStream(sourceStream, leaveOpen: false, bufferSize: TransportDefaults.StreamSmallReadCacheSize)
            : sourceStream, streamId)
    {
        _isServer = isServer;
        _useStandardMasking = useStandardMasking;
    }

    private WebSocketStream(Stream sourceStream, string streamId, int reusedCount, bool isServer, bool useStandardMasking)
        : base(sourceStream, streamId, reusedCount)
    {
        _isServer = isServer;
        _useStandardMasking = useStandardMasking;
    }

    public void EnableInitialPadding()
    {
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);
        if (!_useStandardMasking)
            throw new InvalidOperationException("Initial padding requires negotiated WebSocket masking.");
        if (WroteChunkCount != 0)
            throw new InvalidOperationException("Initial padding must be enabled before the first stream write.");
        _initialPadding ??= new InitialStreamPadding();
        _paddingActive = true;
    }

    // A connection that will not be reused closes its transport without a logical-stream handshake.
    // Its owner must still cancel cover traffic and release this stream's timer/cancellation state.
    internal void StopInitialPadding()
    {
        _paddingActive = false;
        _initialPadding?.Dispose();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // support zero length buffer. It should just call the base stream write
            _readTask = buffer.Length == 0
                ? SourceStream.ReadAsync(buffer, cancellationToken)
                : ReadInternalAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            return await _readTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        // check is stream has finished
        if (_finished)
            return 0;

        // If there are no more in the chunks read the next chunk
        if (_remainingChunkBytes == 0) {
            var header = await ReadChunkHeaderAsync(cancellationToken).Vhc();

            // check the payload length
            if (header.PayloadLength > int.MaxValue)
                throw new InvalidOperationException(
                    $"WebSocket payload length is too big: {header.PayloadLength}. StreamId: {StreamId}");

            _remainingChunkBytes = (int)header.PayloadLength;
            _readMask = _useStandardMasking ? header.MaskKey : ReadOnlyMemory<byte>.Empty;
            _readMaskOffset = 0;

            // check if the stream has been closed
            if (_remainingChunkBytes == 0) {
                _finished = true;
                return 0;
            }
        }

        // Determine the number of bytes to read based on the remaining chunk size and buffer length
        var bytesToRead = Math.Min(_remainingChunkBytes, buffer.Length);
        var bytesRead = await SourceStream.ReadAsync(buffer[..bytesToRead], cancellationToken).Vhc();

        // if bytesRead is 0 and _remainingChunkBytes is not 0, it means the stream has been closed unexpectedly
        if (bytesRead == 0)
            throw new Exception("WebSocketStream has been closed unexpectedly.");

        if (!_readMask.IsEmpty) {
            MaskPayload(buffer.Span[..bytesRead], _readMask.Span, _readMaskOffset);
            _readMaskOffset += bytesRead;
        }

        // update remaining chunk
        _remainingChunkBytes -= bytesRead;
        if (_remainingChunkBytes == 0)
            ReadChunkCount++;

        return bytesRead;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // support zero length buffer. It should just call the base stream write
            _writeTask = WriteSerialized(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    public async ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // should not write a zero chunk if caller data is zero
            _writeTask = WritePreservedSerialized(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async ValueTask WriteSerialized(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);
            if (buffer.IsEmpty)
                await SourceStream.WriteAsync(buffer, cancellationToken).Vhc();
            else
                await WriteInternalAsync(buffer, cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    private async ValueTask WritePreservedSerialized(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < PreserveWriteBufferLength)
            throw new ArgumentException("Buffer length is less than required preserved header length.", nameof(buffer));

        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);
            var data = buffer[PreserveWriteBufferLength..];
            if (data.IsEmpty)
                await SourceStream.WriteAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).Vhc();
            else if (!_paddingActive || !await TryWritePadded(data, cancellationToken).Vhc())
                await WriteInternalPreserverAsync(buffer, cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    private async ValueTask WriteInternalPreserverAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < PreserveWriteBufferLength)
            throw new ArgumentException("Buffer length is less than required preserved header length.", nameof(buffer));

        // create the chunk header
        var header = BuildHeader(_writeChunkHeaderBuffer.Span,
            buffer.Length - PreserveWriteBufferLength);

        // change start if buffer to skip the unused header length
        var finalBuffer = buffer[(PreserveWriteBufferLength - header.Length)..];

        // Copy the header to the start of the buffer
        header.CopyTo(finalBuffer.Span);
        var headerLength = header.Length;

        // Compatibility (2026-10): existing peers, including early protocol 15 builds, set MASK but leave
        // payloads unchanged. Actual masking requires agreement in the WebSocket upgrade response.
        if (!_isServer && _useStandardMasking)
            MaskPayload(buffer.Span[PreserveWriteBufferLength..], header[^4..], 0);

        // The caller owns a preserved buffer and may inspect or reuse its payload after the write.
        // Restore it even when a canceled transport write prevents reuse of this connection.
        try {
            await SourceStream.WriteAsync(finalBuffer, cancellationToken).Vhc();
        }
        finally {
            if (!_isServer && _useStandardMasking)
                MaskPayload(buffer.Span[PreserveWriteBufferLength..],
                    finalBuffer.Span.Slice(headerLength - 4, 4), 0);
        }
        WroteChunkCount++;
    }

    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!buffer.IsEmpty && _paddingActive && await TryWritePadded(buffer, cancellationToken).Vhc())
            return;

        var bufferLength = ChunkHeaderLength + buffer.Length;
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(bufferLength);
        var fullBuffer = memoryOwner.Memory[..bufferLength];
        buffer.CopyTo(fullBuffer[ChunkHeaderLength..]);
        // await required to prevent releasing of the memory owner
        await WriteInternalPreserverAsync(fullBuffer, cancellationToken).Vhc();
    }

    private async ValueTask<bool> TryWritePadded(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_initialPadding is not { } padding)
            return false;
        var paddingLength = padding.TakePaddingLength(data.Length, cover: false);
        _paddingActive = padding.CanPadData;
        if (paddingLength == 0)
            return false;

        // One source write is essential: a separate padding write commonly becomes a separate TLS record.
        // Rent only while encoding/writing, never keep a large buffer on every open proxy stream.
        var rented = ArrayPool<byte>.Shared.Rent(data.Length + paddingLength + 2 * ChunkHeaderLength);
        try {
            var length = EncodeFrame(rented, data.Span, paddingLength: 0, opcode: 2);
            length += EncodeFrame(rented.AsSpan(length), ReadOnlySpan<byte>.Empty, paddingLength, opcode: 1);
            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, padding.CancellationToken);
            await SourceStream.WriteAsync(rented.AsMemory(0, length), writeCts.Token).Vhc();
            WroteChunkCount++;
            padding.StartCover(WriteCover, GetWriteCount);
            return true;
        }
        finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private int GetWriteCount() => WroteChunkCount;

    private int EncodeFrame(Span<byte> buffer, ReadOnlySpan<byte> data, int paddingLength, byte opcode)
    {
        var payloadLength = data.Length + paddingLength;
        var header = BuildHeader(buffer[..ChunkHeaderLength], payloadLength);
        header[0] = (byte)(0x80 | opcode);
        var payload = buffer.Slice(header.Length, payloadLength);
        if (paddingLength == 0)
            data.CopyTo(payload);
        else {
            Random.Shared.NextBytes(payload);
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(' ' + payload[i] % 95); // printable ASCII is always valid UTF-8 text
        }

        if (!_isServer && _useStandardMasking)
            MaskPayload(payload, header[^4..], 0);
        return header.Length + payloadLength;
    }

    private async Task WriteCover(InitialStreamPadding padding, CancellationToken cancellationToken)
    {
        // Skip a busy writer instead of queueing fake traffic ahead of real traffic.
        if (!_writeGate.Wait(0))
            return;
        try {
            if (_disposed || IsDisposed || _isConnectionClosed)
                return;
            var length = padding.TakePaddingLength(0, cover: true);
            if (length == 0)
                return;
            var rented = ArrayPool<byte>.Shared.Rent(length + ChunkHeaderLength);
            try {
                var frameLength = EncodeFrame(rented, ReadOnlySpan<byte>.Empty, length, opcode: 1);
                await SourceStream.WriteAsync(rented.AsMemory(0, frameLength), cancellationToken).Vhc();
            }
            finally {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch (OperationCanceledException ex) {
            // Cancellation can leave part of a frame on the transport. It must not enter the reuse pool.
            CloseByError(ex);
            throw;
        }
        catch (Exception ex) {
            CloseByError(ex); // The cover task owns and observes this failure, just like a data writer.
        }
        finally {
            _writeGate.Release();
        }
    }

    private static void MaskPayload(Span<byte> payload, ReadOnlySpan<byte> key, int offset)
    {
        for (var i = 0; i < payload.Length; i++)
            payload[i] ^= key[(offset + i) & 3];
    }

    private Span<byte> BuildHeader(Span<byte> header, int payloadLength)
    {
        if (_isServer)
            return WebSocketUtils.BuildWebSocketFrameHeader(header, payloadLength);

        Span<byte> maskKey = stackalloc byte[4];
        RandomNumberGenerator.Fill(maskKey);
        return WebSocketUtils.BuildWebSocketFrameHeader(header, payloadLength, maskKey);
    }

    // records only: inside a read or write, a close could only be the sync one; DisposeAsync closes the source
    private void CloseByError(Exception ex)
    {
        _exception ??= ex;
    }

    private async Task<WebSocketHeader> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        // read chunk header by cryptor
        try {
            while (true) {
                // get chunk header
                var webSocketHeader =
                    await WebSocketUtils.ReadWebSocketHeader(SourceStream, _readChunkHeaderBuffer, cancellationToken).Vhc();

                // skip close connection payload
                if (webSocketHeader.IsCloseConnection) {
                    CloseStatus = await ReadCloseStatus(webSocketHeader, cancellationToken).Vhc();
                    VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                        "WebSocketStream has been closed by WebSocket close frame. CloseStatus: {CloseStatus}, StreamId: {StreamId}",
                        CloseStatus, StreamId);

                    _isConnectionClosed = true;
                    _initialPadding?.Stop();
                    return new WebSocketHeader {
                        IsCloseConnection = true,
                        IsBinary = true,
                        PayloadLength = 0
                    };
                }

                if (webSocketHeader.IsPing) {
                    await ReplyPong(webSocketHeader, cancellationToken).Vhc();
                    continue;
                }
                if (webSocketHeader.IsPong) {
                    await DiscardWebSocketFrame(webSocketHeader.PayloadLength, cancellationToken).Vhc();
                    continue;
                }

                // Control frames may interrupt a fragmented message. Only the text message's continuations
                // are discarded: a binary continuation still carries proxy bytes, even after an intervening ping.
                bool isBinary;
                if (webSocketHeader.IsContinuation) {
                    isBinary = _fragmentedBinary ?? throw new InvalidDataException("Unexpected WebSocket continuation.");
                    if (webSocketHeader.IsFinal)
                        _fragmentedBinary = null;
                    if (webSocketHeader.PayloadLength == 0)
                        continue; // An empty final continuation is not our logical-stream terminator.
                }
                else {
                    if (_fragmentedBinary != null)
                        throw new InvalidDataException("A fragmented WebSocket message has not ended.");
                    isBinary = webSocketHeader.IsBinary;
                    if (!webSocketHeader.IsFinal)
                        _fragmentedBinary = isBinary;
                    if (webSocketHeader.PayloadLength == 0 && !webSocketHeader.IsFinal)
                        continue;
                }

                if (!isBinary) {
                    await DiscardWebSocketFrame(webSocketHeader.PayloadLength, cancellationToken).Vhc();
                    continue;
                }
                return webSocketHeader;
            }
        }
        catch (EndOfStreamException) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "WebSocketStream has been closed without terminator. StreamId: {StreamId}", StreamId);
            _isConnectionClosed = true;
            return new WebSocketHeader {
                IsCloseConnection = true,
                IsBinary = true,
                PayloadLength = 0
            };
        }
    }

    private async Task ReplyPong(WebSocketHeader header, CancellationToken cancellationToken)
    {
        var payload = new byte[(int)header.PayloadLength]; // control frames are bounded to 125 bytes
        await SourceStream.ReadExactlyAsync(payload, cancellationToken).Vhc();
        if (!header.MaskKey.IsEmpty)
            MaskPayload(payload, header.MaskKey.Span, 0);
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            if (_isConnectionClosed || IsDisposed)
                return;
            var frame = new byte[ChunkHeaderLength + payload.Length];
            var frameHeader = BuildHeader(frame, payload.Length);
            frameHeader[0] = 0x8a;
            payload.CopyTo(frame, frameHeader.Length);
            if (!_isServer)
                MaskPayload(frame.AsSpan(frameHeader.Length, payload.Length), frameHeader[^4..], 0);
            await SourceStream.WriteAsync(frame.AsMemory(0, frameHeader.Length + payload.Length), cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    private async Task<ushort?> ReadCloseStatus(WebSocketHeader webSocketHeader, CancellationToken cancellationToken)
    {
        if (webSocketHeader.PayloadLength == 1)
            throw new InvalidDataException("A WebSocket close status must contain two bytes.");
        if (webSocketHeader.PayloadLength == 0) {
            await DiscardWebSocketFrame(webSocketHeader.PayloadLength, cancellationToken).Vhc();
            return null;
        }

        var buffer = new byte[2];
        await SourceStream.ReadExactlyAsync(buffer, cancellationToken).Vhc();

        if (!webSocketHeader.MaskKey.IsEmpty) {
            buffer[0] ^= webSocketHeader.MaskKey.Span[0];
            buffer[1] ^= webSocketHeader.MaskKey.Span[1];
        }

        var remaining = webSocketHeader.PayloadLength - 2;
        if (remaining > 0)
            await DiscardWebSocketFrame(remaining, cancellationToken).Vhc();

        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private async Task DiscardWebSocketFrame(long payloadLength, CancellationToken cancellationToken)
    {
        if (payloadLength > int.MaxValue)
            throw new InvalidOperationException(
                $"WebSocket close frame payload length is too big: {payloadLength}. StreamId: {StreamId}");

        if (payloadLength == 0)
            return;
        using var memoryOwner = MemoryPool<byte>.Shared.Rent((int)Math.Min(payloadLength, 1024));
        var memory = memoryOwner.Memory;
        var read = 0;
        while (read < payloadLength) {
            var bytesToRead = (int)Math.Min(payloadLength - read, memory.Length);
            var bytesRead = await SourceStream.ReadAsync(memory[..bytesToRead], cancellationToken).Vhc();
            if (bytesRead == 0)
                throw new EndOfStreamException("Truncated WebSocket frame payload.");
            read += bytesRead;
        }
    }

    public override async Task<ChunkStream> CreateReuse()
    {
        // try to close stream if it is not closed yet
        await DisposeAsync().Vhc();

        // check if there is exception in the stream
        if (_exception != null)
            throw _exception;

        // could not reuse the underlying stream has been closed
        if (_isConnectionClosed)
            throw new EndOfStreamException(
                $"Could not reuse a WebSocketStream that its underling stream has been closed . StreamId: {StreamId}");

        // create a new WebSocketStream with the same source stream and stream id
        return new WebSocketStream(SourceStream, StreamId, ReusedCount + 1, isServer: _isServer,
            useStandardMasking: _useStandardMasking);
    }

    private Task CloseStreamAsync()
    {
        lock (_closeStreamLock)
            _closeStreamTask ??= CloseStreamInternal();
        return _closeStreamTask;
    }

    private async Task CloseStreamInternal()
    {
        try {
            using var timeoutCts = new CancellationTokenSource(TransportDefaults.TcpGracefulTimeout);

            // wait for write to finish and write the terminator
            await _writeTask.AsTask().WaitAsync(timeoutCts.Token).Vhc();

            if (_initialPadding != null)
                await _initialPadding.Completion.WaitAsync(timeoutCts.Token).Vhc();

            // A canceled cover write can have left half a frame. Close the transport instead of adding
            // a logical terminator to an already unusable byte stream.
            if (!CanReuse)
                return;

            // Finish the stream by sending the chunk terminator and 
            await _writeGate.WaitAsync(timeoutCts.Token).Vhc();
            try {
                await WriteInternalAsync(ReadOnlyMemory<byte>.Empty, timeoutCts.Token).Vhc();
            }
            finally {
                _writeGate.Release();
            }

            // wait for the current read task to finish
            await _readTask.AsTask().WaitAsync(timeoutCts.Token).Vhc();

            // wait for end of stream
            await DiscardRemainingStreamData(timeoutCts.Token).Vhc();
        }
        catch (Exception ex) {
            _exception = ex; // indicate that the stream can not be reused
            VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                "Could not close the stream gracefully. StreamId: {StreamId}", StreamId);
            throw;
        }
    }

    private async ValueTask DiscardRemainingStreamData(CancellationToken cancellationToken)
    {
        // read the rest of the stream until it is closed
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(500);
        var trashedLength = 0;
        while (true) {
            var read = await ReadInternalAsync(memoryOwner.Memory, cancellationToken).Vhc();
            if (read == 0)
                break;

            trashedLength += read;
        }

        // Log if the stream has not been closed gracefully
        if (trashedLength > 0) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "Trashing unexpected binary stream data. StreamId: {StreamId}, TrashedLength: {TrashedLength}",
                StreamId, trashedLength);
        }
    }

    // the server's only, as a client's frame must be masked
    public async ValueTask WriteClose(ushort closeStatus, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);
        if (!_isServer)
            throw new InvalidOperationException("Only the server writes a close status.");

        if (_isConnectionClosed)
            return;

        _isConnectionClosed = true;
        _initialPadding?.Stop();

        try {
            await _writeGate.WaitAsync(cancellationToken).Vhc();
            try {
                var closeFrame = WebSocketUtils.BuildServerCloseFrame(closeStatus);
                await SourceStream.WriteAsync(closeFrame, cancellationToken).Vhc();
                await SourceStream.FlushAsync(cancellationToken).Vhc();
            }
            finally {
                _writeGate.Release();
            }
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    // it leaves the source to its owner: closing that synchronously waits for a QUIC stream's peer
    protected override void Dispose(bool disposing)
    {
        _disposed = true; // don't return if already disposed. DisposeAsync may call set this
        _initialPadding?.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end, as its close of the logical stream writes to the source
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (IsDisposed)
            return;

        _disposed = true;
        _initialPadding?.Stop();

        // just ignore exceptions during close stream as close stream already handles logging
        if (CanReuse)
            await VhInvoker.TryInvokeAsync("close the WebSocket stream", LogLevel.Debug, CloseStreamAsync).Vhc();

        // one that cannot carry the next logical stream, as its peer ended it or a read, a write or its end failed,
        // closes its source, which ends what still waits on it; its owner's close of the transport finds it closed
        if (!CanReuse && !IsDisposed)
            await CloseSourceAsync().Vhc();

        if (_initialPadding != null) {
            // The graceful path already joined the cover task under its close deadline. If that failed,
            // the source has been closed instead; do not add an unbounded second wait for a failed writer.
            _initialPadding.Dispose();
        }

        await base.DisposeAsync().Vhc();
    }
}
