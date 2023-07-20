using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Channels.Streams;

public class BinaryStream : ChunkStream
{
    private const int ChunkHeaderLength = 5;
    private int _remainingChunkBytes;
    private bool _disposed;
    private bool _finished;
    private bool _hasError;
    private readonly CancellationTokenSource _readCts = new();
    private readonly CancellationTokenSource _writeCts = new();
    private Task<int> _readTask = Task.FromResult(0);
    private Task _writeTask = Task.CompletedTask;
    private bool _isConnectionClosed;
    private bool _isCurrentReadingChunkEncrypted;
    private readonly StreamCryptor _streamCryptor;
    private readonly byte[] _readChunkHeaderBuffer = new byte[ChunkHeaderLength];
    private byte[] _writeBuffer = Array.Empty<byte>();
    
    public long MaxEncryptChunk { get; set; } = long.MaxValue;
    public override int PreserveWriteBufferLength => ChunkHeaderLength;

    //todo buffer size
    public BinaryStream(Stream sourceStream, string streamId, byte[] secret)
        : base(new ReadCacheStream(sourceStream, false, TunnelDefaults.StreamProxyBufferSize), streamId)
    {
        _streamCryptor = StreamCryptor.Create(SourceStream, secret, leaveOpen: true, encryptInGivenBuffer: true);
    }

    private BinaryStream(Stream sourceStream, string streamId, StreamCryptor streamCryptor, int reusedCount)
        : base(sourceStream, streamId, reusedCount)
    {
        _streamCryptor = streamCryptor;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_readCts.Token, cancellationToken);
        _readTask = ReadInternalAsync(buffer, offset, count, tokenSource.Token);
        return await _readTask; // await needed to dispose tokenSource
    }

    private async Task<int> ReadInternalAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            // check is stream has finished
            if (_finished)
                return 0;

            // If there are no more in the chunks read the next chunk
            if (_remainingChunkBytes == 0)
            {
                _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken);
                _finished = _remainingChunkBytes == 0;
                
                // check last chunk
                if (_finished)
                    return 0;
            }

            var bytesToRead = Math.Min(_remainingChunkBytes, count);
            var stream = _isCurrentReadingChunkEncrypted ? _streamCryptor : SourceStream;
            var bytesRead = await stream.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
            if (bytesRead == 0 && count != 0) // count zero is used for checking the connection
                throw new Exception("BinaryStream has been closed unexpectedly.");

            // update remaining chunk
            _remainingChunkBytes -= bytesRead;
            if (_remainingChunkBytes == 0) ReadChunkCount++;
            return bytesRead;
        }
        catch (Exception ex)
        {
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
        if (!await StreamUtil.ReadWaitForFillAsync(_streamCryptor, _readChunkHeaderBuffer, 0, _readChunkHeaderBuffer.Length, cancellationToken))
        {
            if (!_finished && ReadChunkCount > 0)
                VhLogger.Instance.LogWarning(GeneralEventId.TcpLife, "BinaryStream has been closed unexpectedly.");
            _isConnectionClosed = true;
            return 0;
        }

        // get chunk size
        var chunkSize = BitConverter.ToInt32(_readChunkHeaderBuffer);
        _isCurrentReadingChunkEncrypted = _readChunkHeaderBuffer[4] == 1;
        return chunkSize;

    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_finished)
            throw new SocketException((int)SocketError.ConnectionAborted);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_writeCts.Token, cancellationToken);

        // should not write a zero chunk if caller data is zero
        try
        {
            _writeTask = count == 0
                ? SourceStream.WriteAsync(buffer, offset, count, tokenSource.Token)
                : WriteInternalAsync(buffer, offset, count, tokenSource.Token);

            await _writeTask;
        }
        catch (Exception ex)
        {
            CloseByError(ex);
            throw;
        }
    }

    private async Task WriteInternalAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            var encryptChunk = MaxEncryptChunk > 0;

            if (PreserveWriteBuffer)
            {
                // create the chunk header and encrypt it
                BitConverter.GetBytes(count).CopyTo(buffer, offset - ChunkHeaderLength);
                buffer[4] = encryptChunk ? (byte)1 : (byte)0;
                _streamCryptor.Encrypt(buffer, 0, ChunkHeaderLength); //header should always be encrypted

                // encrypt chunk
                if (encryptChunk)
                    _streamCryptor.Encrypt(buffer, offset, count);

                await SourceStream.WriteAsync(buffer, offset - ChunkHeaderLength, ChunkHeaderLength + count, cancellationToken);
            }
            else
            {
                var size = ChunkHeaderLength + count;
                if (size > _writeBuffer.Length)
                    Array.Resize(ref _writeBuffer, size);

                // create the chunk header and encrypt it
                BitConverter.GetBytes(count).CopyTo(_writeBuffer, 0);
                _writeBuffer[4] = encryptChunk ? (byte)1 : (byte)0;
                _streamCryptor.Encrypt(_writeBuffer, 0, ChunkHeaderLength); //header should always be encrypted

                // add buffer to chunk and encrypt
                Buffer.BlockCopy(buffer, offset, _writeBuffer, ChunkHeaderLength, count);
                if (encryptChunk)
                    _streamCryptor.Encrypt(_writeBuffer, ChunkHeaderLength, count);

                // Copy write buffer to output
                await SourceStream.WriteAsync(_writeBuffer, 0, size, cancellationToken);
            }

            // make sure chunk is sent
            await SourceStream.FlushAsync(cancellationToken);
            WroteChunkCount++;
            if (MaxEncryptChunk > 0) MaxEncryptChunk--;
        }
        catch (Exception ex)
        {
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
            throw new InvalidOperationException($"Could not reuse a BinaryStream that its underling stream has been closed . StreamId: {StreamId}");

        // Dispose the stream but keep the original stream open
        _leaveOpen = true;
        await DisposeAsync();

        // reuse if the stream has been closed gracefully
        if (_finished && !_hasError)
            return new BinaryStream(SourceStream, StreamId, _streamCryptor, ReusedCount + 1);

        // dispose and throw the ungraceful BinaryStream
        await base.DisposeAsync();
        throw new InvalidOperationException($"Could not reuse a BinaryStream that has not been closed gracefully. StreamId: {StreamId}");
    }

    private async Task CloseStream(CancellationToken cancellationToken)
    {
        // cancel writing requests. 
        _readCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);
        _writeCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);

        try { await _writeTask; }
        catch { /* Ignore */ }
        _writeCts.Dispose();

        // finish writing current BinaryStream gracefully
        try
        {
            await WriteInternalAsync(Array.Empty<byte>(), 0, 0, cancellationToken);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Could not write the chunk terminator. StreamId: {StreamId}",
                StreamId);
        }

        // make sure current caller read has been finished gracefully or wait for cancellation time
        try { await _readTask; }
        catch { /* Ignore */ }

        try
        {
            if (_hasError)
                throw new InvalidOperationException("Could not close a BinaryStream due internal error.");

            if (_remainingChunkBytes != 0)
                throw new InvalidOperationException("Attempt to dispose a BinaryStream before finishing the current chunk.");

            if (!_finished)
            {
                var buffer = new byte[10];
                var read = await ReadInternalAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read != 0)
                    throw new InvalidDataException("BinaryStream read unexpected data on end.");
            }
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "BinaryStream has not been closed gracefully. StreamId: {StreamId}",
                StreamId);
        }
    }

    private readonly AsyncLock _disposeLock = new();
    private ValueTask? _disposeTask;
    public override ValueTask DisposeAsync()
    {
        lock (_disposeLock)
            _disposeTask ??= DisposeAsyncCore();
        return _disposeTask.Value;
    }

    private async ValueTask DisposeAsyncCore()
    {
        // prevent other caller requests
        _disposed = true;

        // close stream
        if (!_hasError && !_isConnectionClosed)
        {
            // create a new cancellation token for CloseStream
            using var cancellationTokenSource = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);
            await CloseStream(cancellationTokenSource.Token);
        }

        if (!_leaveOpen)
            await base.DisposeAsync();
    }
}