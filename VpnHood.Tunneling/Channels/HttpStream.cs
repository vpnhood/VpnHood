using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Channels;

public class HttpStream : AsyncStreamDecorator
{
    private readonly byte[] _newLineBytes = "\r\n"u8.ToArray();

    private int _remainingChunkBytes;
    private readonly byte[] _chunkHeaderBuffer = new byte[10];
    private readonly byte[] _nextLineBuffer = new byte[2];
    private bool _disposed;
    private bool _isHttpHeaderSent;
    private bool _isHttpCloseConnectionSent;
    private bool _isHttpHeaderRead;
    private bool _isFinished;
    private readonly string? _host;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly TimeSpan _disposeTimeout = TimeSpan.FromSeconds(10); //todo
    private Task<int> _readTask = Task.FromResult(0);
    private Task _writeTask = Task.CompletedTask;

    private bool IsServer => _host == null;
    public int ReadChunkCount { get; private set; }
    public int WroteChunkCount { get; private set; }
    public bool IsCloseRequested { get; private set; }
    public string StreamId { get; internal set; }

    public HttpStream(Stream sourceStream, string streamId, string? host, bool keepSourceOpen = false)
        //: base(new ReadCacheStream(sourceStream, keepSourceOpen, 512), false) //todo
        : base(sourceStream, false)
    {
        _host = host;
        StreamId = streamId;
    }

    private HttpStream(Stream sourceStream, string streamId, string? host)
        : base(sourceStream, false)
    {
        _host = host;
        StreamId = streamId;
    }

    private string CreateHttpHeader(bool closeConnection)
    {
        var connection = closeConnection ? "Connection: close" : "Connection: keep-alive";

        if (IsServer)
        {
            return
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                $"{connection}\r\n" +
                "\r\n";
        }

        return
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Host: {_host}\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            "Cache-Control: no-store\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            $"{connection}\r\n" +
            "\r\n";
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = tokenSource.Token;

        _readTask = ReadInternalAsync(buffer, offset, count, cancellationToken);
        return _readTask;
    }

    private async Task<int> ReadInternalAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // ignore header
        if (!_isHttpHeaderRead)
        {
            var headers = await ReadHeadersAsync(SourceStream, cancellationToken);
            IsCloseRequested =
                headers.TryGetValue("Connection", out var connectionValue)
                && connectionValue.Equals("close", StringComparison.OrdinalIgnoreCase);

            _isHttpHeaderRead = true;
        }

        // check is stream has finished
        if (_isFinished)
            return 0;

        // If there are no more in the chunks read the next chunk
        if (_remainingChunkBytes == 0)
            _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken);

        // check last chunk
        _isFinished = _remainingChunkBytes == 0;
        if (_isFinished)
            return 0;

        var bytesToRead = Math.Min(_remainingChunkBytes, count);
        var bytesRead = await SourceStream.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
        if (bytesRead == 0 && count != 0) // count zero is used for checking the connection
            throw new Exception("HTTP Stream closed unexpectedly.");

        _remainingChunkBytes -= bytesRead;
        return bytesRead;
    }

    private async Task<int> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {
        try
        {
            // read the end of last chunk if it is not first chunk
            if (ReadChunkCount != 0)
                await ReadNextLine(cancellationToken);

            // read chunk
            var bufferOffset = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (bufferOffset == _chunkHeaderBuffer.Length) throw new InvalidDataException("Chunk header exceeds the maximum size.");
                var bytesRead = await SourceStream.ReadAsync(_chunkHeaderBuffer, bufferOffset, 1, cancellationToken);

                if (bytesRead == 0)
                    throw new InvalidDataException("Could not read HTTP Chunk header.");

                if (bufferOffset > 0 && _chunkHeaderBuffer[bufferOffset - 1] == '\r' && _chunkHeaderBuffer[bufferOffset] == '\n')
                    break;

                bufferOffset++;
            }

            var chunkSizeHex = Encoding.ASCII.GetString(_chunkHeaderBuffer, 0, bufferOffset - 1);
            if (!int.TryParse(chunkSizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize))
                throw new InvalidDataException("Invalid HTTP chunk size.");

            if (chunkSize == 0)
                await ReadNextLine(cancellationToken); //read the end of stream
            else
                ReadChunkCount++;

            return chunkSize;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = tokenSource.Token;

        // should not call zero chunk if caller data is zero
        if (count == 0)
            return SourceStream.WriteAsync(buffer, offset, count, cancellationToken);

        _writeTask = WriteInternalAsync(buffer, offset, count, false, cancellationToken);
        return _writeTask;
    }


    private async Task WriteInternalAsync(byte[] buffer, int offset, int count, bool closeConnection, CancellationToken cancellationToken)
    {
        // write header for first time
        if (!_isHttpHeaderSent)
        {
            await SourceStream.WriteAsync(Encoding.UTF8.GetBytes(CreateHttpHeader(closeConnection)), cancellationToken);
            _isHttpHeaderSent = true;
            _isHttpCloseConnectionSent = closeConnection;
        }

        // Write the chunk header
        var headerBytes = Encoding.ASCII.GetBytes(count.ToString("X") + "\r\n");
        await SourceStream.WriteAsync(headerBytes, cancellationToken);

        // Write the chunk data
        await SourceStream.WriteAsync(buffer, offset, count, cancellationToken);

        // Write the chunk footer
        await SourceStream.WriteAsync(_newLineBytes, cancellationToken);
        await FlushAsync(cancellationToken);

        WroteChunkCount++;
    }

    private static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream,
        CancellationToken cancellationToken, int maxLength = 8192)
    {
        // read header
        using var memStream = new MemoryStream(1024);
        var readBuffer = new byte[1];
        var lfCounter = 0;
        while (lfCounter < 4)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, 1, cancellationToken);
            if (bytesRead == 0)
                throw new Exception("HTTP Stream closed unexpectedly!");

            if (readBuffer[0] == '\r' || readBuffer[0] == '\n')
                lfCounter++;
            else
                lfCounter = 0;

            await memStream.WriteAsync(readBuffer, 0, 1, cancellationToken);

            if (memStream.Length > maxLength)
                throw new Exception("HTTP header is too big.");
        }

        memStream.Position = 0;
        var reader = new StreamReader(memStream, Encoding.UTF8);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Read the header lines until an empty line is encountered
        while (true)
        {
            // ReSharper disable once MethodHasAsyncOverload
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                break;

            // Split the header line into header field and value
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0)
            {
                var headerField = line[..separatorIndex].Trim();
                var headerValue = line[(separatorIndex + 1)..].Trim();
                headers[headerField] = headerValue;
            }
        }

        return headers;
    }


    private async Task ReadNextLine(CancellationToken cancellationToken)
    {
        var bytesRead = await SourceStream.ReadAsync(_nextLineBuffer, 0, 2, cancellationToken);
        if (bytesRead < 2 || _nextLineBuffer[0] != '\r' || _nextLineBuffer[1] != '\n')
            throw new InvalidDataException("Could not find expected line feed in HTTP chunk header.");
    }

    private bool _keepOpen;
    public async Task<HttpStream> CreateReuse()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (IsCloseRequested)
            throw new InvalidOperationException("Could not reuse a HttpStream in close connection state.");

        // Dispose the stream but keep the original stream open
        _keepOpen = true;
        await DisposeAsync();

        // reuse if the stream has been closed gracefully
        if (_isFinished)
            return new HttpStream(SourceStream, StreamId,  _host);

        // dispose and throw the ungraceful http stream
        await base.DisposeAsync();
        throw new InvalidOperationException("Could not reuse a HttpStream that has not been closed gracefully.");
    }

    private async Task CloseStream(bool closeConnection, CancellationToken cancellationToken)
    {
        // finish writing current http stream gracefully
        try
        {
            await WriteInternalAsync(Array.Empty<byte>(), 0, 0, closeConnection, cancellationToken);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Could not write the HTTP chunk terminator. StreamId: {StreamId}", 
                StreamId);
        }

        if (!closeConnection || _isHttpCloseConnectionSent)
            return;

        // try to send Connection close if it is not sent
        try
        {
            await WriteInternalAsync(Array.Empty<byte>(), 0, 0, closeConnection, cancellationToken);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Could not close connection by writing the HTTP chunk terminator. StreamId: {StreamId}", 
                StreamId);
        }

    }

    public override ValueTask DisposeAsync()
    {
        return DisposeAsync(false);
    }

    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync(bool closeConnection)
    {
        using var locker = await _disposeLock.LockAsync(TimeSpan.Zero);
        if (_disposed || !locker.Succeeded) return;
        _disposed = true;

        // cancel all caller requests
        _cancellationTokenSource.Cancel();
        await Task.WhenAll(_readTask, _writeTask);

        using var cancellationTokenSource = new CancellationTokenSource(_disposeTimeout);

        // send http close connection
        await CloseStream(closeConnection, cancellationTokenSource.Token);

        // make sure the stream in finished gracefully
        try
        {
            if (_remainingChunkBytes != 0)
                throw new InvalidOperationException("Attempt to dispose a HTTP stream before finishing the current chunk.");

            if (!_isFinished)
            {
                var buffer = new byte[2048];
                while (true)
                {
                    var read = await ReadInternalAsync(buffer, 0, buffer.Length, cancellationTokenSource.Token);
                    if (read == 0) break;
                    VhLogger.Instance.LogWarning(GeneralEventId.TcpLife,
                        "HttpStream trashing unexpected data after disposing. StreamId: {StreamId}, DataLength: {DataLength}",
                        StreamId, read);
                }
            }
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex, 
                "HttpStream has not been closed gracefully. StreamId: {StreamId}", 
                StreamId);
        }

        if (!_keepOpen)
            await base.DisposeAsync();
    }
}