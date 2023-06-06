using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Channels;

public class HttpStream : AsyncStreamDecorator
{
    private readonly byte[] _newLineBytes = "\r\n"u8.ToArray();

    private int _remainingChunkBytes;
    private readonly byte[] _chunkHeaderBuffer = new byte[10];
    private readonly Stream _writeBuffer;
    private readonly byte[] _nextLineBuffer = new byte[2];
    private bool _disposed;
    private readonly string _httpHeader;
    private bool _isHttpHeaderWritten;
    private bool _isHttpHeaderRead;
    private bool _isFinished;

    public int ReadChunkCount { get; private set; }
    public int WroteChunkCount { get; private set; }

    public HttpStream(Stream sourceStream, bool keepOpen, string? host)
        : base(new ReadCacheStream(sourceStream, keepOpen, 512), false)
    {
        _writeBuffer = sourceStream; // don't use write buffer in this version
        if (string.IsNullOrEmpty(host))
        {
            _httpHeader =
                $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
                $"Host: {host}\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n";
        }
        else
        {
            _httpHeader =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Cache-Control: no-store\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n";
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // ignore header
        if (!_isHttpHeaderRead)
        {
            await IgnoreHeadersAsync(cancellationToken);
            _isHttpHeaderRead = true;
        }

        // start reading chunks
        var totalBytesRead = 0;
        while (count > 0 && !_isFinished)
        {
            // If there are remaining bytes from the previous chunk, copy them to the output buffer
            if (_remainingChunkBytes > 0)
            {
                var bytesToCopy = Math.Min(_remainingChunkBytes, count);
                var bytesRead = await SourceStream.ReadAsync(buffer, offset, bytesToCopy, cancellationToken);

                _remainingChunkBytes -= bytesRead;
                count -= bytesRead;
                offset += bytesRead;
                totalBytesRead += bytesRead;

                // If the output buffer is filled, return the total bytes read
                if (count == 0)
                    return totalBytesRead;
            }

            // If there are no more in the chunks read the next chunk
            if (_remainingChunkBytes == 0)
                _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken);

            // If the remaining chunk bytes is zero, it indicates the end of the stream
            _isFinished = _remainingChunkBytes == 0;
        }

        return totalBytesRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // write header for first time
        if (!_isHttpHeaderWritten)
        {
            await _writeBuffer.WriteAsync(Encoding.UTF8.GetBytes(_httpHeader), cancellationToken);
            _isHttpHeaderWritten = true;
        }

        // Write the chunk header
        var headerBytes = Encoding.ASCII.GetBytes(count.ToString("X") + "\r\n");
        await _writeBuffer.WriteAsync(headerBytes, cancellationToken);

        // Write the chunk data
        await _writeBuffer.WriteAsync(buffer, offset, count, cancellationToken);

        // Write the chunk footer
        await _writeBuffer.WriteAsync(_newLineBytes, cancellationToken);
        await FlushAsync(cancellationToken);
        WroteChunkCount++;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (WroteChunkCount > 0)
        {
            await SourceStream.WriteAsync("0\r\n\r\n"u8.ToArray());
            await FlushAsync();
        }

        await base.DisposeAsync();
    }

    private async Task<int> ReadChunkHeaderAsync(CancellationToken cancellationToken)
    {

        // read the end of last chunk if it is not first chunk
        if (ReadChunkCount != 0)
            await ReadNextLine(cancellationToken);

        // read chunk
        var bufferOffset = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            // seek for \r
            if (++bufferOffset == _chunkHeaderBuffer.Length) throw new InvalidDataException("Chunk header exceeds the maximum size.");
            var bytesRead = await SourceStream.ReadAsync(_chunkHeaderBuffer, bufferOffset, 1, cancellationToken);

            if (bytesRead == 0) return 0; //no more chunk
            if (_chunkHeaderBuffer[bufferOffset] != '\r') continue;

            // seek for \n
            if (++bufferOffset == _chunkHeaderBuffer.Length) throw new InvalidDataException("Chunk header exceeds the maximum size.");
            bytesRead = await SourceStream.ReadAsync(_chunkHeaderBuffer, ++bufferOffset, 1, cancellationToken);
            if (bytesRead == 0) throw new InvalidDataException("Invalid HTTP Chunk header.");
            if (_chunkHeaderBuffer[bufferOffset] == '\n')
                break;
        }

        var chunkSizeHex = Encoding.ASCII.GetString(_chunkHeaderBuffer, 0, bufferOffset - 1);
        if (!int.TryParse(chunkSizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize))
            throw new InvalidDataException("Invalid HTTP chunk size.");

        if (chunkSize != 0)
            ReadChunkCount++;

        return chunkSize;
    }

    private async Task IgnoreHeadersAsync(CancellationToken cancellationToken, int maxLength = 8192)
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

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _writeBuffer.FlushAsync(cancellationToken);
    }
}