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
    private readonly string _httpHeader;
    private bool _isHttpHeaderWritten;
    private bool _isHttpHeaderRead;
    private bool _isFinished;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public int ReadChunkCount { get; private set; }
    public int WroteChunkCount { get; private set; }

    public HttpStream(Stream sourceStream, string? host, bool keepSourceOpen = false)
        : base(new ReadCacheStream(sourceStream, keepSourceOpen, 512), false)
    {
        if (string.IsNullOrEmpty(host))
        {
            _httpHeader =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n";
        }
        else
        {
            _httpHeader =
                $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
                $"Host: {host}\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n";
        }
    }

    private HttpStream(Stream sourceStream, string httpHeader)
        : base(sourceStream, false)
    {
        _httpHeader = httpHeader;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = tokenSource.Token;

        // ignore header
        if (!_isHttpHeaderRead)
        {
            await IgnoreHeadersAsync(cancellationToken);
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
        if (bytesRead == 0)
            throw new Exception("HTTP Stream closed unexpectedly.");

        _remainingChunkBytes -= bytesRead;
        return bytesRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        // Create CancellationToken
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        cancellationToken = tokenSource.Token;

        // write header for first time
        if (!_isHttpHeaderWritten)
        {
            await SourceStream.WriteAsync(Encoding.UTF8.GetBytes(_httpHeader), cancellationToken);
            _isHttpHeaderWritten = true;
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

    private async Task IgnoreHeadersAsync(CancellationToken cancellationToken, int maxLength = 8192)
    {
        try
        {
            var readBuffer = new byte[1];
            var lfCounter = 0;
            var headerLength = 0;
            while (lfCounter < 4)
            {
                var bytesRead = await SourceStream.ReadAsync(readBuffer, 0, 1, cancellationToken);
                if (bytesRead == 0)
                    throw new Exception("HTTP Stream closed unexpectedly!");

                if (readBuffer[0] == '\r' || readBuffer[0] == '\n')
                    lfCounter++;
                else
                    lfCounter = 0;

                if (headerLength++ > maxLength)
                    throw new Exception("HTTP header is too big.");
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    // ReSharper disable once UnusedMember.Local
    private static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream,
        CancellationToken cancellationToken = default, int maxLength = 8192)
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
            throw new InvalidDataException("Invalid HTTP chunk.");
    }

    private bool _keepOpen;
    public async Task<HttpStream> CreateReuse()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        _keepOpen = true;
        await DisposeAsync();
        return new HttpStream(SourceStream, _httpHeader);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // finish current chunk
        try
        {
            if (!_isFinished) //_isFinished mean the peer already disposed the stream and does not listen anymore
            {
                await SourceStream.WriteAsync("0\r\n\r\n"u8.ToArray());
                await FlushAsync();
            }
        }
        catch(Exception ex)
        {
            VhLogger.LogError(GeneralEventId.TcpLife, ex, "Could not write HTTP chunk terminator.");
        }

        // cancel all requests
        _cancellationTokenSource.Cancel();

        if (!_keepOpen)
            await base.DisposeAsync();
    }
}