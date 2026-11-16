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

/// <summary>
/// Represents a stream in WebSocket frames over another stream, binary frames for its data and an empty final one for
/// its end, so the other stream can go on to carry the next stream.
/// </summary>
/// <remarks>
/// Its graceful close, <see cref="DisposeAsync"/>, ends it with its peer; one that cannot carry the next stream, as its
/// peer ended it or a read, a write or its end failed, closes its source instead.
/// </remarks>
public class WebSocketStream : ChunkStream, IPreservedChunkStream
{
    private readonly bool _isServer;
    private readonly bool _useStandardMasking;
    private readonly WebSocketFrameWriter _frameWriter;
    private ReadOnlyMemory<byte> _readMask;
    private int _readMaskOffset;
    private bool? _fragmentedBinary;
    private const int ChunkHeaderLength = WebSocketFrameWriter.MaximumHeaderLength;
    private int _remainingChunkBytes;
    private bool _finished;
    private Exception? _exception;
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;
    private readonly Memory<byte> _readChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private Task? _closeStreamTask;
    private readonly Lock _closeStreamLock = new();
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;
    internal override bool CanReuse => !_isConnectionClosed && _exception == null && _frameWriter.Failure == null;

    /// <summary>
    /// Gets the length a buffer for <see cref="WritePreservedAsync"/> keeps free before its data, for the frame's
    /// header.
    /// </summary>
    public int PreserveWriteBufferLength => ChunkHeaderLength;

    /// <summary>Gets a value indicating whether the client's frames are masked as the WebSocket standard has it.</summary>
    public bool UsesStandardMasking => _useStandardMasking;
    internal bool HasWrittenInitialPadding => _frameWriter.HasWrittenInitialPadding;

    /// <summary>Gets the status the peer's close frame carried, or <see langword="null"/> where it carried none.</summary>
    public ushort? CloseStatus { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="WebSocketStream"/> class.</summary>
    /// <param name="sourceStream">
    /// The stream that carries the frames, which a close leaves open where the stream can carry the next one.
    /// </param>
    /// <param name="streamId">The stream's ID, which its logs carry.</param>
    /// <param name="useBuffer"><see langword="true"/> to read the source through a small read buffer.</param>
    /// <param name="isServer"><see langword="true"/> for the server's side of the stream.</param>
    /// <param name="useStandardMasking">
    /// <see langword="true"/> to mask the client's frames as the WebSocket standard has it.
    /// </param>
    public WebSocketStream(Stream sourceStream, string streamId, bool useBuffer, bool isServer,
        bool useStandardMasking = false)
        : base(useBuffer
            ? new ReadBufferedStream(sourceStream, leaveOpen: false, bufferSize: TransportDefaults.StreamSmallReadCacheSize)
            : sourceStream, streamId)
    {
        _isServer = isServer;
        _useStandardMasking = useStandardMasking;
        _frameWriter = new WebSocketFrameWriter(SourceStream, isServer, useStandardMasking);
    }

    private WebSocketStream(Stream sourceStream, string streamId, int reusedCount, bool isServer, bool useStandardMasking)
        : base(sourceStream, streamId, reusedCount)
    {
        _isServer = isServer;
        _useStandardMasking = useStandardMasking;
        _frameWriter = new WebSocketFrameWriter(SourceStream, isServer, useStandardMasking);
    }

    /// <summary>Turns on the padding of the stream's first writes, which hides their lengths.</summary>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The stream's masking is not the standard one, or it has written already.
    /// </exception>
    public void EnableInitialPadding()
    {
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);
        _frameWriter.EnableInitialPadding();
    }

    /// <inheritdoc/>
    /// <remarks>It reads the data of the binary frames; the stream's end frame, or the peer's close, ends it.</remarks>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    /// <exception cref="InvalidOperationException">A frame is longer than the stream reads.</exception>
    /// <exception cref="InvalidDataException">The frames break the WebSocket protocol.</exception>
    /// <exception cref="Exception">The source ended inside a frame.</exception>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // An empty read reaches the source without consuming a frame.
            _readTask = buffer.Length == 0
                ? SourceStream.ReadAsync(buffer, cancellationToken)
                : ReadInternalAsync(buffer, cancellationToken);

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
            WebSocketUtils.MaskPayload(buffer.Span[..bytesRead], _readMask.Span, _readMaskOffset);
            _readMaskOffset += bytesRead;
        }

        // update remaining chunk
        _remainingChunkBytes -= bytesRead;
        if (_remainingChunkBytes == 0)
            ReadChunkCount++;

        return bytesRead;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It writes the data as one binary frame, and nothing for none; a failure, but a token already canceled when it is
    /// called, leaves the stream unable to carry the next.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            // Graceful close waits for the current data write before sending its terminator.
            _writeTask = _frameWriter.Write(buffer, cancellationToken);

            await _writeTask.Vhc();
            WroteChunkCount = _frameWriter.WroteChunkCount;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    /// <summary>
    /// Writes data as one binary frame; with no padding, its header goes in the room the buffer keeps free before the
    /// data, so the frame goes out without a copy. With no data, it writes nothing.
    /// </summary>
    /// <param name="buffer">
    /// The buffer: <see cref="PreserveWriteBufferLength"/> bytes of room, then the data. The write may overwrite both,
    /// even when it fails.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the write ends.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buffer"/> is shorter than <see cref="PreserveWriteBufferLength"/>.
    /// </exception>
    public async ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed || _disposed, this);

        try {
            _writeTask = _frameWriter.WritePreserved(buffer, cancellationToken);

            await _writeTask.Vhc();
            WroteChunkCount = _frameWriter.WroteChunkCount;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
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
                    _frameWriter.Stop();
                    return new WebSocketHeader {
                        IsCloseConnection = true,
                        IsBinary = true,
                        PayloadLength = 0
                    };
                }

                if (webSocketHeader.IsPing || webSocketHeader.IsPong) {
                    await DiscardWebSocketFrame(webSocketHeader.PayloadLength, cancellationToken).Vhc();
                    continue;
                }

                // Control frames may interrupt a fragmented message. Only the text message's continuations
                // are discarded: a binary continuation still carries forwarding bytes, even after an intervening ping.
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
                    if (webSocketHeader is { PayloadLength: 0, IsFinal: false })
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

    internal override async Task<ChunkStream> CreateReuse()
    {
        // try to close stream if it is not closed yet
        await DisposeAsync().Vhc();

        // check if there is exception in the stream
        if (_exception != null)
            throw _exception;
        if (_frameWriter.Failure is { } writeFailure)
            throw writeFailure;

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

            await _frameWriter.PaddingCompletion.WaitAsync(timeoutCts.Token).Vhc();

            // A canceled cover write can have left half a frame. Close the transport instead of adding
            // a logical terminator to an already unusable byte stream.
            if (!CanReuse)
                return;

            await _frameWriter.WriteTerminator(timeoutCts.Token).Vhc();
            WroteChunkCount = _frameWriter.WroteChunkCount;

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

    /// <summary>Writes a close frame with a status, which ends the connection, not just the stream.</summary>
    /// <param name="closeStatus">The status.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the frame is written; with the connection already closed, at once.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The stream is the client's: only the server writes a close status, as a client's frame must be masked.
    /// </exception>
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
            await _writeTask.AsTask().WaitAsync(cancellationToken).Vhc();
            await _frameWriter.WriteClose(closeStatus, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It leaves the source to its owner: closing that synchronously waits for a QUIC stream's peer.</remarks>
    protected override void Dispose(bool disposing)
    {
        _disposed = true; // don't return if already disposed. DisposeAsync may call set this
        _frameWriter.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It ends the stream with its peer where it can carry the next one, and closes the source where it cannot. A second
    /// call waits for the first one's end.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end, as its close of the logical stream writes to the source
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (IsDisposed)
            return;

        _disposed = true;
        _frameWriter.Stop();

        // just ignore exceptions during close stream as close stream already handles logging
        if (CanReuse)
            await VhInvoker.TryInvokeAsync("close the WebSocket stream", LogLevel.Debug, CloseStreamAsync).Vhc();

        // one that cannot carry the next logical stream, as its peer ended it or a read, a write or its end failed,
        // closes its source, which ends what still waits on it; its owner's close of the transport finds it closed
        if (!CanReuse && !IsDisposed)
            await CloseSourceAsync().Vhc();

        // Graceful close joined cover under its deadline. A failed close ended the source instead;
        // disposing the writer cancels local state without an unbounded second wait for native I/O.
        _frameWriter.Dispose();

        await base.DisposeAsync().Vhc();
    }
}
