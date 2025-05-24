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
    private readonly CancellationTokenSource _disposeCts = new();
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;
    private readonly Memory<byte> _readChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private ValueTask? _closeStreamTask;
    private readonly object _closeStreamLock = new();
    public override bool CanReuse => !_isConnectionClosed && _exception == null && AllowReuse;
    public int PreserveWriteBufferLength => ChunkHeaderLength;

    public BinaryStreamStandard(Stream sourceStream, string streamId, bool useBuffer)
        : base(useBuffer
            ? new ReadCacheStream(sourceStream, false, TunnelDefaults.StreamProxyBufferSize)
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
        // check if the stream has been disposed
        if (_disposeCts.IsCancellationRequested)
            throw new ObjectDisposedException(GetType().Name);

        try {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);

            // support zero length buffer. It should just call the base stream write
            _readTask = buffer.Length == 0
                ? SourceStream.ReadAsync(buffer, tokenSource.Token)
                : ReadInternalAsync(buffer, tokenSource.Token);

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
        // check if the stream has been disposed
        if (_disposeCts.IsCancellationRequested) 
            throw new ObjectDisposedException(GetType().Name);

        try {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);

            // support zero length buffer. It should just call the base stream write
            _writeTask = buffer.Length == 0
                ? SourceStream.WriteAsync(buffer, tokenSource.Token)
                : WriteInternalAsync(buffer, tokenSource.Token);

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
        // check if the stream has been disposed
        if (_disposeCts.IsCancellationRequested) 
            throw new ObjectDisposedException(GetType().Name);

        try {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);

            // should not write a zero chunk if caller data is zero
            _writeTask = buffer.Length == PreserveWriteBufferLength
                ? SourceStream.WriteAsync(buffer, tokenSource.Token)
                : WriteInternalPreserverAsync(buffer, tokenSource.Token);

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

        VhLogger.Instance.LogError(GeneralEventId.TcpLife, ex,
            "Disposing BinaryStream. StreamId: {StreamId}", StreamId);
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
        // make sure it is disposed
        await DisposeAsync();

        // check if there is exception in the stream
        if (_exception != null)
            throw _exception;

        // check if the stream can be reused
        if (!CanReuse)
            throw new InvalidOperationException("Can not reuse the stream.");

        // try to close stream if it is not closed yet
        lock (_closeStreamLock)
            _closeStreamTask ??= CloseStream();

        // ReSharper disable once InconsistentlySynchronizedField
        await _closeStreamTask.Value;

        // could not reuse the underlying stream has been closed
        if (_isConnectionClosed) {
            throw new EndOfStreamException(
                $"Could not reuse a BinaryStream that its underling stream has been closed . StreamId: {StreamId}");
        }

        return new BinaryStreamStandard(SourceStream, StreamId, ReusedCount + 1);
    }

    private async ValueTask CloseStream()
    {
        if (!_disposeCts.IsCancellationRequested)
            throw new InvalidOperationException("Could not close this stream before it is disposed.");

        try {
            // wait for write and read tasks to finish and return to caller otherwise it will throw pending tasks exception
            // We just wait for its final return
            try { await _writeTask.VhConfigureAwait(); }
            catch{ /* ignore */}
            try { await _readTask.VhConfigureAwait(); }
            catch { /*ignore */}

            using var timeoutCts = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);

            // Finish the stream by sending the chunk terminator and 
            await WriteInternalAsync(ReadOnlyMemory<byte>.Empty, timeoutCts.Token).VhConfigureAwait();

            // wait for the sub stream to be closed
            if (!_finished)
                await DiscardRemainingStreamData(timeoutCts.Token);
        }
        catch (Exception ex) {
            _exception = ex; // indicate that the stream can not be reused
            VhLogger.Instance.LogError(GeneralEventId.TcpLife, ex,
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
        if (!_disposeCts.IsCancellationRequested && disposing) {
            _disposeCts.Cancel();
            if (CanReuse) {
                // let it run in the background, the stream owner will close it
                try {
                    lock (_closeStreamLock)
                        _closeStreamTask ??= CloseStream();
                }
                catch {
                    // CloseStream will already dispose the stream on any exception
                }
            }
            else {
                SourceStream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

}