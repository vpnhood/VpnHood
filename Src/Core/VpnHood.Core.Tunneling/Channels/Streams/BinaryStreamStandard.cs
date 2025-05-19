using System.Buffers;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels.Streams;

public class BinaryStreamStandard : ChunkStream, IPreservedChunkStream
{
    private const int ChunkHeaderLength = 4;
    private int _remainingChunkBytes;
    private bool _finished;
    private bool _hasError;
    private readonly CancellationTokenSource _readCts = new();
    private readonly CancellationTokenSource _writeCts = new();
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;
    private readonly byte[] _readChunkHeaderBuffer = new byte[ChunkHeaderLength];

    public int PreserveWriteBufferLength => ChunkHeaderLength;

    public BinaryStreamStandard(Stream sourceStream, string streamId, bool useBuffer)
        : base(
            useBuffer ? new ReadCacheStream(sourceStream, false, TunnelDefaults.StreamProxyBufferSize) : sourceStream,
            streamId)
    {
    }

    private BinaryStreamStandard(Stream sourceStream, string streamId, int reusedCount)
        : base(sourceStream, streamId, reusedCount)
    {
    }


    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_readCts.Token, cancellationToken);
        _readTask = ReadInternalAsync(buffer, tokenSource.Token);
        return await _readTask.VhConfigureAwait(); // await needed to dispose tokenSource
    }

    private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try {
            // check is stream has finished
            if (_finished)
                return 0;

            // If there are no more in the chunks read the next chunk
            if (_remainingChunkBytes == 0) {
                _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken).VhConfigureAwait();
                _finished = _remainingChunkBytes == 0;

                // check last chunk
                if (_finished)
                    return 0;
            }

            var bytesToRead = Math.Min(_remainingChunkBytes, buffer.Length);
            var bytesRead = await SourceStream.ReadAsync(buffer[..bytesToRead], cancellationToken)
                .VhConfigureAwait();
            if (bytesRead == 0 && buffer.Length != 0) // count zero is used for checking the connection
                throw new Exception("BinaryStream has been closed unexpectedly.");

            // update remaining chunk
            _remainingChunkBytes -= bytesRead;
            if (_remainingChunkBytes == 0) ReadChunkCount++;
            return bytesRead;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private void CloseByError(Exception ex)
    {
        if (_hasError) return;
        _hasError = true;

        VhLogger.LogError(GeneralEventId.TcpLife, ex, "Disposing BinaryStream. StreamId: {StreamId}", StreamId);
        _ = DisposeAsync();
    }

    private async Task<int> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        // read chunk header by cryptor
        if (!await StreamUtils.ReadExactAsync(SourceStream, _readChunkHeaderBuffer, 0, _readChunkHeaderBuffer.Length, cancellationToken)
                .VhConfigureAwait()) {

            if (!_finished && ReadChunkCount > 0)
                VhLogger.Instance.LogWarning(GeneralEventId.TcpLife, "BinaryStream has been closed unexpectedly.");
            _isConnectionClosed = true;
            return 0;
        }

        // get chunk size
        var chunkSize = BitConverter.ToInt32(_readChunkHeaderBuffer);
        return chunkSize;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (_finished) throw new SocketException((int)SocketError.ConnectionAborted);
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_writeCts.Token, cancellationToken);

        // should not write a zero chunk if caller data is zero
        try {
            _writeTask = buffer.Length == 0
                ? SourceStream.WriteAsync(buffer, tokenSource.Token)
                : WriteInternalAsync(buffer, tokenSource.Token);

            await _writeTask.VhConfigureAwait();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    public async ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (_finished) throw new SocketException((int)SocketError.ConnectionAborted);
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_writeCts.Token, cancellationToken);

        // should not write a zero chunk if caller data is zero
        try {
            _writeTask = buffer.Length == PreserveWriteBufferLength
                ? SourceStream.WriteAsync(buffer, tokenSource.Token)
                : WriteInternalPreserverAsync(buffer, tokenSource.Token);

            await _writeTask.VhConfigureAwait();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async ValueTask WriteInternalPreserverAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try {
            // create the chunk header
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Span, buffer.Length - PreserveWriteBufferLength);
            await SourceStream.WriteAsync(buffer, cancellationToken).VhConfigureAwait();
            await SourceStream.FlushAsync(cancellationToken).VhConfigureAwait();
            WroteChunkCount++;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        try {
            var bufferLength = ChunkHeaderLength + buffer.Length;
            using var memoryOwner = MemoryPool<byte>.Shared.Rent(bufferLength);
            var fullBuffer = memoryOwner.Memory[..bufferLength];

            // create the chunk header
            BinaryPrimitives.WriteInt32LittleEndian(fullBuffer.Span, buffer.Length);

            // prepend the buffer to chunk
            buffer.CopyTo(fullBuffer[ChunkHeaderLength..]);

            // Copy write buffer to output
            await SourceStream.WriteAsync(fullBuffer, cancellationToken).VhConfigureAwait();

            // make sure chunk is sent
            await SourceStream.FlushAsync(cancellationToken).VhConfigureAwait();
            WroteChunkCount++;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private bool _leaveOpen;
    public override bool CanReuse => !_hasError && !_isConnectionClosed;

    public override async Task<ChunkStream> CreateReuse()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_hasError)
            throw new InvalidOperationException($"Could not reuse a BinaryStream that has error. StreamId: {StreamId}");

        if (_isConnectionClosed)
            throw new InvalidOperationException(
                $"Could not reuse a BinaryStream that its underling stream has been closed . StreamId: {StreamId}");

        // Dispose the stream but keep the original stream open
        _leaveOpen = true;
        await DisposeAsync().VhConfigureAwait();

        // reuse if the stream has been closed gracefully
        if (_finished && !_hasError)
            return new BinaryStreamStandard(SourceStream, StreamId, ReusedCount + 1);

        // dispose and throw the ungraceful BinaryStream
        await base.DisposeAsync().VhConfigureAwait();
        throw new InvalidOperationException(
            $"Could not reuse a BinaryStream that has not been closed gracefully. StreamId: {StreamId}");
    }

    private async Task CloseStream(CancellationToken cancellationToken)
    {
        // cancel writing requests. 
        _readCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);
        _writeCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);

        // wait for finishing current write
        try {
            await _writeTask.VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Final stream write has not been completed gracefully. StreamId: {StreamId}",
                StreamId);
            return;
        }

        _writeCts.Dispose();

        // finish writing current BinaryStream gracefully
        try {
            await WriteInternalAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Could not write the chunk terminator. StreamId: {StreamId}",
                StreamId);
            return;
        }

        // make sure current caller read has been finished gracefully or wait for cancellation time
        try {
            await _readTask.VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Final stream read has not been completed gracefully. StreamId: {StreamId}",
                StreamId);
            return;
        }

        _readCts.Dispose();

        try {
            if (_hasError)
                throw new InvalidOperationException("Could not close a BinaryStream due internal error.");

            if (!_finished) {
                var buffer = new byte[500];
                var trashedLength = 0;
                while (true) {
                    var read = await ReadInternalAsync(buffer, cancellationToken).VhConfigureAwait();
                    if (read == 0)
                        break;

                    trashedLength += read;
                }

                if (trashedLength > 0)
                    VhLogger.Instance.LogWarning(GeneralEventId.TcpLife,
                        "Trashing unexpected binary stream data. StreamId: {StreamId}, TrashedLength: {TrashedLength}",
                        StreamId, trashedLength);
            }
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "BinaryStream has not been closed gracefully. StreamId: {StreamId}",
                StreamId);
        }
    }

    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();

    public override async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        // close stream
        if (!_hasError && !_isConnectionClosed) {
            // create a new cancellation token for CloseStream
            using var cancellationTokenSource = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);
            await CloseStream(cancellationTokenSource.Token).VhConfigureAwait();
        }

        if (!_leaveOpen)
            await base.DisposeAsync().VhConfigureAwait();
    }
}