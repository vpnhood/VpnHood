using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Binary;
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

    // the status the peer's close frame carried, if it carried one
    public ushort? CloseStatus { get; private set; }

    public WebSocketStream(Stream sourceStream, string streamId, bool useBuffer, bool isServer)
        : base(useBuffer
            ? new ReadBufferedStream(sourceStream, leaveOpen: false, bufferSize: TransportDefaults.StreamSmallReadCacheSize)
            : sourceStream, streamId)
    {
        _isServer = isServer;
    }

    private WebSocketStream(Stream sourceStream, string streamId, int reusedCount, bool isServer)
        : base(sourceStream, streamId, reusedCount)
    {
        _isServer = isServer;
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
            _writeTask = buffer.Length == 0
                ? SourceStream.WriteAsync(buffer, cancellationToken)
                : WriteInternalAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    public async ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // should not write a zero chunk if caller data is zero
            _writeTask = buffer.Length == PreserveWriteBufferLength
                ? SourceStream.WriteAsync(buffer, cancellationToken)
                : WriteInternalPreserverAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async ValueTask WriteInternalPreserverAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < PreserveWriteBufferLength)
            throw new ArgumentException("Buffer length is less than required preserved header length.", nameof(buffer));

        // create the chunk header
        var header = BuildHeader(_writeChunkHeaderBuffer.Span,
            buffer.Length - PreserveWriteBufferLength, GenerateNewMaskKey(stackalloc byte[4]));

        // change start if buffer to skip the unused header length
        var finalBuffer = buffer[(PreserveWriteBufferLength - header.Length)..];

        // Copy the header to the start of the buffer
        header.CopyTo(finalBuffer.Span);

        // write the buffer
        await SourceStream.WriteAsync(finalBuffer, cancellationToken).Vhc();
        WroteChunkCount++;
    }

    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var bufferLength = ChunkHeaderLength + buffer.Length;
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(bufferLength);
        var fullBuffer = memoryOwner.Memory[..bufferLength];
        buffer.CopyTo(fullBuffer[ChunkHeaderLength..]);
        // await required to prevent releasing of the memory owner
        await WriteInternalPreserverAsync(fullBuffer, cancellationToken).Vhc();
    }

    private Span<byte> BuildHeader(Span<byte> header, int payloadLength, ReadOnlySpan<byte> maskKey)
    {
        // Build the WebSocket frame header
        return _isServer
            ? WebSocketUtils.BuildWebSocketFrameHeader(header, payloadLength)
            : WebSocketUtils.BuildWebSocketFrameHeader(header, payloadLength, maskKey);
    }

    private static Span<byte> GenerateNewMaskKey(Span<byte> buffer)
    {
        if (buffer.Length < 4)
            throw new ArgumentException("Buffer must be at least 4 bytes long.", nameof(buffer));

        // Generate a new mask key
        var random = Random.Shared;
        for (var i = 0; i < 4; i++)
            buffer[i] = (byte)random.Next(0, 256);

        return buffer;
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
                    return new WebSocketHeader {
                        IsCloseConnection = true,
                        IsBinary = true,
                        PayloadLength = 0
                    };
                }

                // read another chunk header if it is not data chunk
                if (webSocketHeader.IsPing || webSocketHeader.IsPing || !webSocketHeader.IsBinary) {
                    VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                        "WebSocketStream has received a WebSocket frame that is not binary. StreamId: {StreamId}",
                        StreamId);

                    // discard the frame payload if it is ping or pong
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

    private async Task<ushort?> ReadCloseStatus(WebSocketHeader webSocketHeader, CancellationToken cancellationToken)
    {
        if (webSocketHeader.PayloadLength < 2) {
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

        using var memoryOwner = MemoryPool<byte>.Shared.Rent(1024 * 8);
        var memory = memoryOwner.Memory;
        var read = 0;
        while (read < payloadLength) {
            var bytesToRead = (int)Math.Min(payloadLength - read, memory.Length);
            var bytesRead = await SourceStream.ReadAsync(memory[..bytesToRead], cancellationToken).Vhc();
            if (bytesRead == 0)
                break; // end of stream
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
        return new WebSocketStream(SourceStream, StreamId, ReusedCount + 1, isServer: _isServer);
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

            // Finish the stream by sending the chunk terminator and 
            await WriteInternalAsync(ReadOnlyMemory<byte>.Empty, timeoutCts.Token).Vhc();

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

        try {
            var closeFrame = WebSocketUtils.BuildServerCloseFrame(closeStatus);
            await SourceStream.WriteAsync(closeFrame, cancellationToken).Vhc();
            await SourceStream.FlushAsync(cancellationToken).Vhc();
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
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end, as its close of the logical stream writes to the source
        using var scopeLock = await _disposeLock.LockAsync().Vhc();
        if (IsDisposed)
            return;

        _disposed = true;

        // just ignore exceptions during close stream as close stream already handles logging
        if (CanReuse)
            await VhInvoker.TryInvokeAsync("close the WebSocket stream", LogLevel.Debug, CloseStreamAsync).Vhc();

        // one that cannot carry the next logical stream, as its peer ended it or a read, a write or its end failed,
        // closes its source, which ends what still waits on it; its owner's close of the transport finds it closed
        if (!CanReuse && !IsDisposed)
            await CloseSourceAsync().Vhc();

        await base.DisposeAsync().Vhc();
    }
}