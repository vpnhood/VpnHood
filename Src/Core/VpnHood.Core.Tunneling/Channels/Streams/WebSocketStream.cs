using Microsoft.Extensions.Logging;
using System.Buffers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.WebSockets;

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
    private readonly object _closeStreamLock = new();
    private int _isDisposed;
    public override bool CanReuse => !_isConnectionClosed && _exception == null && AllowReuse;
    public int PreserveWriteBufferLength => ChunkHeaderLength;

    public WebSocketStream(Stream sourceStream, string streamId, bool useBuffer, bool isServer)
        : base(useBuffer
            ? new ReadCacheStream(sourceStream, leaveOpen: false, TunnelDefaults.StreamProxyBufferSize)
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
        ObjectDisposedException.ThrowIf(IsDisposed || _isDisposed == 1, this);

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
            throw new Exception("BinaryStream has been closed unexpectedly.");

        // update remaining chunk
        _remainingChunkBytes -= bytesRead;
        if (_remainingChunkBytes == 0)
            ReadChunkCount++;

        return bytesRead;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed || _isDisposed == 1, this);

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
        ObjectDisposedException.ThrowIf(IsDisposed || _isDisposed == 1, this);

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
        await WriteInternalPreserverAsync(fullBuffer, cancellationToken);

        // create the chunk header
        // var header = BuildHeader(_writeChunkHeaderBuffer.Span, buffer.Length, GenerateNewMaskKey(stackalloc byte[4]));
        // await SourceStream.WriteAsync(_writeChunkHeaderBuffer[..header.Length], cancellationToken).Vhc();
        // await SourceStream.WriteAsync(buffer, cancellationToken).Vhc();
        //WroteChunkCount++;
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

    private void CloseByError(Exception ex)
    {
        if (_exception != null) return;
        _exception = ex;
        Dispose();
    }

    private async Task<WebSocketHeader> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        // read chunk header by cryptor
        try {
            while (true) {
                // get chunk header
                var webSocketHeader = await WebSocketUtils.ReadWebSocketHeader(SourceStream, _readChunkHeaderBuffer, cancellationToken);

                // skip close connection payload
                if (webSocketHeader.IsCloseConnection) {
                    VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                        "BinaryStream has been closed by WebSocket close frame. StreamId: {StreamId}", StreamId);

                    // discard the frame payload
                    await DiscardWebSocketFrame(webSocketHeader, cancellationToken).Vhc();

                    _isConnectionClosed = true;
                    return new WebSocketHeader {
                        IsCloseConnection = true,
                        IsBinary = true,
                        PayloadLength = 0
                    };
                }

                // read another chunk header if it is not data chunk
                if (webSocketHeader.IsPing || webSocketHeader.IsPing || !webSocketHeader.IsBinary) {
                    VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                        "BinaryStream has received a WebSocket frame that is not binary. StreamId: {StreamId}", StreamId);

                    // discard the frame payload if it is ping or pong
                    await DiscardWebSocketFrame(webSocketHeader, cancellationToken).Vhc();
                    continue;
                }

                return webSocketHeader;
            }
        }
        catch (EndOfStreamException) {
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "BinaryStream has been closed without terminator. StreamId: {StreamId}", StreamId);
            _isConnectionClosed = true;
            return new WebSocketHeader {
                IsCloseConnection = true,
                IsBinary = true,
                PayloadLength = 0
            };
        }
    }

    private async Task DiscardWebSocketFrame(WebSocketHeader webSocketHeader, CancellationToken cancellationToken)
    {
        if (webSocketHeader.PayloadLength > int.MaxValue)
            throw new InvalidOperationException($"WebSocket close frame payload length is too big: {webSocketHeader.PayloadLength}. StreamId: {StreamId}");

        using var memoryOwner = MemoryPool<byte>.Shared.Rent(1024 * 8);
        var memory = memoryOwner.Memory;
        var read = 0;
        while (read < webSocketHeader.PayloadLength) {
            var bytesToRead = (int)Math.Min(webSocketHeader.PayloadLength - read, memory.Length);
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
                $"Could not reuse a BinaryStream that its underling stream has been closed . StreamId: {StreamId}");

        // check if the stream can be reused
        if (!CanReuse)
            throw new InvalidOperationException("Can not reuse the stream.");

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
            using var timeoutCts = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);

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
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife, ex,
                "Could not close the stream gracefully. StreamId: {StreamId}", StreamId);
            await SourceStream.DisposeAsync().Vhc();
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
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "Trashing unexpected binary stream data. StreamId: {StreamId}, TrashedLength: {TrashedLength}",
                StreamId, trashedLength);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _isDisposed = 1; // don't return if already disposed. DisposeAsync may call set this

        if (disposing) {
            if (CanReuse) {
                // let it run in the background, the stream owner will close it
                // CloseStream already handles the disposal and logging
                CloseStreamAsync().ContinueWith(_ => { });
            }
            else {
                SourceStream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _isDisposed = 1;

        // just ignore exceptions during close stream as close stream already handles logging
        if (CanReuse)
            await VhUtils.TryInvokeAsync(null, CloseStreamAsync).Vhc();

        await base.DisposeAsync().Vhc();
    }
}