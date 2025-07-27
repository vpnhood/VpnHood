using System.Globalization;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Channels.Streams;

public class HttpStream : ChunkStream
{
    private readonly byte[] _newLineBytes = "\r\n"u8.ToArray();

    private int _remainingChunkBytes;
    private readonly byte[] _chunkHeaderBuffer = new byte[10];
    private readonly byte[] _nextLineBuffer = new byte[2];
    private bool _isHttpHeaderSent;
    private bool _isHttpHeaderRead;
    private bool _isFinished;
    private bool _hasError;
    private readonly string? _host;
    private readonly CancellationTokenSource _readCts = new();
    private readonly CancellationTokenSource _writeCts = new();
    private ValueTask<int> _readTask;
    private ValueTask _writeTask;
    private bool _isConnectionClosed;

    private bool IsServer => _host == null;

    public HttpStream(Stream sourceStream, string streamId, string? host, bool keepSourceOpen = false)
        : base(new ReadCacheStream(sourceStream, keepSourceOpen, cacheSize: TunnelDefaults.StreamSmallReadCacheSize), streamId)
    {
        _host = host;
    }

    private HttpStream(Stream sourceStream, string streamId, string? host)
        : base(sourceStream, streamId)
    {
        _host = host;
    }

    private string CreateHttpHeader()
    {
        if (IsServer) {
            return
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n";
        }

        return
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Host: {_host}\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            "Cache-Control: no-store\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n";
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_readCts.Token, cancellationToken);
        _readTask = ReadInternalAsync(buffer, tokenSource.Token);
        return await _readTask.Vhc(); // await needed to dispose tokenSource
    }

    private async ValueTask<int> ReadInternalAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try {
            // ignore header
            if (!_isHttpHeaderRead) {
                using var headerBuffer =
                    await HttpUtil.ReadHeadersAsync(SourceStream, cancellationToken).Vhc();
                if (headerBuffer.Length == 0) {
                    _isFinished = true;
                    _isConnectionClosed = true;
                    return 0;
                }

                _isHttpHeaderRead = true;
            }

            // check is stream has finished
            if (_isFinished)
                return 0;

            // If there are no more in the chunks read the next chunk
            if (_remainingChunkBytes == 0)
                _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken).Vhc();

            // check last chunk
            _isFinished = _remainingChunkBytes == 0;
            if (_isFinished)
                return 0;

            var bytesToRead = Math.Min(_remainingChunkBytes, buffer.Length);
            var bytesRead = await SourceStream.ReadAsync(buffer[..bytesToRead], cancellationToken)
                .Vhc();
            if (bytesRead == 0 && buffer.Length != 0) // count zero is used for checking the connection
                throw new Exception("HttpStream has been closed unexpectedly.");

            _remainingChunkBytes -= bytesRead;
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

        VhLogger.LogError(GeneralEventId.Stream, ex, "Disposing HttpStream. StreamId: {StreamId}", StreamId);
        _ = DisposeAsync();
    }

    private async Task<int> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        // read the end of last chunk if it is not first chunk
        if (ReadChunkCount != 0)
            await ReadNextLine(cancellationToken).Vhc();

        // read chunk
        var bufferOffset = 0;
        while (!cancellationToken.IsCancellationRequested) {
            if (bufferOffset == _chunkHeaderBuffer.Length)
                throw new InvalidDataException("Chunk header exceeds the maximum size.");
            var bytesRead = await SourceStream.ReadAsync(_chunkHeaderBuffer, bufferOffset, 1, cancellationToken)
                .Vhc();

            if (bytesRead == 0)
                throw new InvalidDataException("Could not read HTTP Chunk header.");

            if (bufferOffset > 0 && _chunkHeaderBuffer[bufferOffset - 1] == '\r' &&
                _chunkHeaderBuffer[bufferOffset] == '\n')
                break;

            bufferOffset++;
        }

        var chunkSizeHex = Encoding.ASCII.GetString(_chunkHeaderBuffer, 0, bufferOffset - 1);
        if (!int.TryParse(chunkSizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize))
            throw new InvalidDataException("Invalid HTTP chunk size.");

        if (chunkSize == 0)
            await ReadNextLine(cancellationToken).Vhc(); //read the end of stream
        else
            ReadChunkCount++;

        return chunkSize;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_isFinished)
            throw new SocketException((int)SocketError.ConnectionAborted);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_writeCts.Token, cancellationToken);

        // should not write a zero chunk if caller data is zero
        try {
            _writeTask = buffer.Length == 0
                ? SourceStream.WriteAsync(buffer, tokenSource.Token)
                : WriteInternalAsync(buffer, tokenSource.Token);

            await _writeTask.Vhc();
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }


    private async ValueTask WriteInternalAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        try {
            // write header for first time
            if (!_isHttpHeaderSent) {
                await SourceStream.WriteAsync(Encoding.UTF8.GetBytes(CreateHttpHeader()), cancellationToken)
                    .Vhc();
                _isHttpHeaderSent = true;
            }

            // Write the chunk header
            var headerBytes = Encoding.ASCII.GetBytes(buffer.Length.ToString("X") + "\r\n");
            await SourceStream.WriteAsync(headerBytes, cancellationToken).Vhc();

            // Write the chunk data
            await SourceStream.WriteAsync(buffer, cancellationToken).Vhc();

            // Write the chunk footer
            await SourceStream.WriteAsync(_newLineBytes, cancellationToken).Vhc();
            await FlushAsync(cancellationToken).Vhc();

            WroteChunkCount++;
        }
        catch (Exception ex) {
            CloseByError(ex);
            throw;
        }
    }

    private async Task ReadNextLine(CancellationToken cancellationToken)
    {
        var bytesRead = await SourceStream.ReadAsync(_nextLineBuffer, 0, 2, cancellationToken).Vhc();
        if (bytesRead < 2 || _nextLineBuffer[0] != '\r' || _nextLineBuffer[1] != '\n')
            throw new InvalidDataException("Could not find expected line feed in HTTP chunk header.");
    }

    private bool _keepOpen;
    public override bool CanReuse => !_hasError && !_isConnectionClosed;

    public override async Task<ChunkStream> CreateReuse()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_hasError)
            throw new InvalidOperationException($"Could not reuse a HttpStream that has error. StreamId: {StreamId}");

        if (_isConnectionClosed)
            throw new InvalidOperationException(
                $"Could not reuse a HttpStream that its underling stream has been closed . StreamId: {StreamId}");

        // Dispose the stream but keep the original stream open
        _keepOpen = true;
        await DisposeAsync().Vhc();

        // reuse if the stream has been closed gracefully
        if (_isFinished && !_hasError)
            return new HttpStream(SourceStream, StreamId, _host);

        // dispose and throw the ungraceful HttpStream
        await base.DisposeAsync().Vhc();
        throw new InvalidOperationException(
            $"Could not reuse a HttpStream that has not been closed gracefully. StreamId: {StreamId}");
    }

    private async Task CloseStream(CancellationToken cancellationToken)
    {
        // cancel writing requests. 
        _readCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);
        _writeCts.CancelAfter(TunnelDefaults.TcpGracefulTimeout);

        try {
            await _writeTask.Vhc();
        }
        catch {
            /* Ignore */
        }

        _writeCts.Dispose();

        // finish writing current HttpStream gracefully
        try {
            await WriteInternalAsync(Memory<byte>.Empty,  cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.Stream, ex,
                "Could not write the HTTP chunk terminator. StreamId: {StreamId}",
                StreamId);
        }

        // make sure current caller read has been finished gracefully or wait for cancellation time
        try {
            await _readTask.Vhc();
        }
        catch {
            /* Ignore */
        }

        try {
            if (_hasError)
                throw new InvalidOperationException("Could not close a HttpStream due internal error.");

            if (_remainingChunkBytes != 0)
                throw new InvalidOperationException(
                    "Attempt to dispose a HttpStream before finishing the current chunk.");

            if (!_isFinished) {
                Memory<byte> buffer = new byte[10];
                var read = await ReadInternalAsync(buffer, cancellationToken).Vhc();
                if (read != 0)
                    throw new InvalidDataException("HttpStream read unexpected data on end.");
            }
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.Stream, ex,
                "HttpStream has not been closed gracefully. StreamId: {StreamId}",
                StreamId);
        }
    }

    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();

    public override async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().Vhc();
        if (_disposed) return;
        _disposed = true;

        // close stream
        if (!_hasError && !_isConnectionClosed) {
            // create a new cancellation token for CloseStream
            using var cancellationTokenSource = new CancellationTokenSource(TunnelDefaults.TcpGracefulTimeout);
            await CloseStream(cancellationTokenSource.Token).Vhc();
        }

        if (!_keepOpen)
            await base.DisposeAsync().Vhc();
    }
}