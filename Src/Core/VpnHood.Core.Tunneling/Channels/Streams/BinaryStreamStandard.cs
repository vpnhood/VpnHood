using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Binary;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
// ReSharper disable InconsistentlySynchronizedField

namespace VpnHood.Core.Tunneling.Channels.Streams;

public class BinaryStreamStandard : ChunkStream, IPreservedChunkStream
{
    private const int ChunkHeaderLength = 4;
    private int _remainingChunkBytes;
    private bool _finished;
    private Exception? _exception;
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;
    private readonly Memory<byte> _readChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private Task? _closeStreamTask;
    private readonly object _closeStreamLock = new();
    public override bool CanReuse => !_isConnectionClosed && _exception == null && AllowReuse;
    public int PreserveWriteBufferLength => ChunkHeaderLength;

    public BinaryStreamStandard(Stream sourceStream, string streamId, bool useBuffer)
        : base(useBuffer
            ? new ReadCacheStream(sourceStream, leaveOpen: false, TunnelDefaults.StreamProxyBufferSize)
            : sourceStream, streamId)
    {
    }

    private BinaryStreamStandard(Stream sourceStream, string streamId, int reusedCount)
        : base(sourceStream, streamId, reusedCount)
    {
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(BinaryStreamStandard));

        try {
            // support zero length buffer. It should just call the base stream write
            _readTask = buffer.Length == 0
                ? SourceStream.ReadAsync(buffer, cancellationToken)
                : ReadInternalAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            return await _readTask.VhConfigureAwait();
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
            _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken).VhConfigureAwait();

            // check if the stream has been closed
            if (_remainingChunkBytes == 0) {
                _finished = true;
                return 0;
            }
        }

        // Determine the number of bytes to read based on the remaining chunk size and buffer length
        var bytesToRead = Math.Min(_remainingChunkBytes, buffer.Length);
        var bytesRead = await SourceStream.ReadAsync(buffer[..bytesToRead], cancellationToken)
            .VhConfigureAwait();

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
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(BinaryStreamStandard));

        try {
            // support zero length buffer. It should just call the base stream write
            _writeTask = buffer.Length == 0
                ? SourceStream.WriteAsync(buffer, cancellationToken)
                : WriteInternalAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.VhConfigureAwait();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    public async ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(BinaryStreamStandard));

        try {
            // should not write a zero chunk if caller data is zero
            _writeTask = buffer.Length == PreserveWriteBufferLength
                ? SourceStream.WriteAsync(buffer, cancellationToken)
                : WriteInternalPreserverAsync(buffer, cancellationToken);

            // await needed to dispose tokenSource
            await _writeTask.VhConfigureAwait();
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
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Span, buffer.Length - PreserveWriteBufferLength);
        await SourceStream.WriteAsync(buffer, cancellationToken).VhConfigureAwait();
        WroteChunkCount++;
    }

    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var bufferLength = ChunkHeaderLength + buffer.Length;
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(bufferLength);
        var fullBuffer = memoryOwner.Memory[..bufferLength];

        // create the chunk header
        BinaryPrimitives.WriteInt32LittleEndian(fullBuffer.Span, buffer.Length);

        // prepend the buffer to chunk
        buffer.CopyTo(fullBuffer[ChunkHeaderLength..]);

        // Copy write buffer to output
        await SourceStream.WriteAsync(fullBuffer, cancellationToken).VhConfigureAwait();
        WroteChunkCount++;
    }

    private void CloseByError(Exception ex)
    {
        if (_exception != null) return;
        _exception = ex;
        Dispose();
    }

    private async Task<int> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        // read chunk header by cryptor
        try {
            await StreamUtils.ReadExactAsync(SourceStream, _readChunkHeaderBuffer, cancellationToken).VhConfigureAwait();
        }
        catch (EndOfStreamException) {
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "BinaryStream has been closed without terminator. StreamId: {StreamId}", StreamId);
            _isConnectionClosed = true;
            return 0;
        }

        // get chunk size
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(_readChunkHeaderBuffer.Span);
        return chunkSize;
    }

    public override async Task<ChunkStream> CreateReuse()
    {
        // try to close stream if it is not closed yet
        await DisposeAsync();

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

        // create a new BinaryStreamStandard with the same source stream and stream id
        return new BinaryStreamStandard(SourceStream, StreamId, ReusedCount + 1);
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
            await _writeTask.AsTask().VhWait(timeoutCts.Token).VhConfigureAwait();

            // Finish the stream by sending the chunk terminator and 
            await WriteInternalAsync(ReadOnlyMemory<byte>.Empty, timeoutCts.Token).VhConfigureAwait();

            // wait for the current read task to finish
            await _readTask.AsTask().VhWait(timeoutCts.Token).VhConfigureAwait();
            
            // wait for end of stream
            await DiscardRemainingStreamData(timeoutCts.Token);
        }
        catch (Exception ex) {
            _exception = ex; // indicate that the stream can not be reused
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife, ex,
                "Could not close the stream gracefully. StreamId: {StreamId}", StreamId);
            await SourceStream.DisposeAsync();
            throw;
        }
    }

    private async ValueTask DiscardRemainingStreamData(CancellationToken cancellationToken)
    {
        // read the rest of the stream until it is closed
        Memory<byte> buffer = new byte[500];
        var trashedLength = 0;
        while (true) {
            var read = await ReadInternalAsync(buffer, cancellationToken).VhConfigureAwait();
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
        if (disposing) {
            if (CanReuse) {
                // let it run in the background, the stream owner will close it
                // CloseStream already handles the disposal and logging
                CloseStreamAsync().ContinueWith(_ => { });
            }
            else {
                SourceStream.Dispose();
                Console.WriteLine($"Destroying original stream. {StreamId}"); //todo
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        // just ignore exceptions during close stream as close stream already handles logging
        if (CanReuse)
            await VhUtils.TryInvokeAsync(null, CloseStreamAsync).VhConfigureAwait();

        await base.DisposeAsync().VhConfigureAwait();
    }
}