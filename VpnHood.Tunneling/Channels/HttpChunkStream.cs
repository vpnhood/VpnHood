using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Tunneling.Channels;

public class HttpChunkStream : Stream
{
    private readonly byte[] _newLineBytes = "\r\n"u8.ToArray();

    private readonly Stream _sourceStream;
    private readonly bool _keepOpen;
    private int _remainingChunkBytes;
    private readonly byte[] _chunkHeaderBuffer = new byte[10];
    private readonly Stream _writeBuffer;
    private readonly byte[] _nextLineBuffer = new byte[2];
    private bool _disposed;

    public int ReadChunkCount { get; private set; }
    public int WroteChunkCount { get; private set; }

    public HttpChunkStream(Stream stream, bool keepOpen = true)
    {
        _sourceStream = stream;
        _keepOpen = keepOpen;
        _writeBuffer = stream; // don't use write buffer in this version
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;

        while (count > 0)
        {
            // If there are remaining bytes from the previous chunk, copy them to the output buffer
            if (_remainingChunkBytes > 0)
            {
                var bytesToCopy = Math.Min(_remainingChunkBytes, count);
                var bytesRead = await _sourceStream.ReadAsync(buffer, offset, bytesToCopy, cancellationToken);

                _remainingChunkBytes -= bytesRead;
                count -= bytesRead;
                offset += bytesRead;
                totalBytesRead += bytesRead;

                // If the output buffer is filled, return the total bytes read
                if (count == 0)
                    return totalBytesRead;
            }

            // If there are no more chunks, read the next chunk
            if (_remainingChunkBytes == 0)
                _remainingChunkBytes = await ReadChunkHeaderAsync(cancellationToken);

            // If the remaining chunk bytes is zero, it indicates the end of the stream
            if (_remainingChunkBytes == 0)
                break;
        }

        return totalBytesRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
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

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // Write the chunk header
        var headerBytes = Encoding.ASCII.GetBytes(buffer.Length.ToString("X") + "\r\n");
        await _writeBuffer.WriteAsync(headerBytes, cancellationToken);

        // Write the chunk data
        await _writeBuffer.WriteAsync(buffer, cancellationToken);

        // Write the chunk footer
        await _writeBuffer.WriteAsync(_newLineBytes, cancellationToken);
        await FlushAsync(cancellationToken);
        WroteChunkCount++;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        // Write the chunk header
        var headerBytes = Encoding.ASCII.GetBytes(count.ToString("X") + "\r\n");
        _writeBuffer.Write(headerBytes, 0, headerBytes.Length);

        // Write the chunk data
        _writeBuffer.Write(buffer, offset, count);

        // Write the chunk footer
        _writeBuffer.Write(_newLineBytes);
        Flush();
        WroteChunkCount++;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            if (WroteChunkCount > 0)
            {
                _sourceStream.Write("0\r\n\r\n"u8.ToArray());
                Flush();
            }

            if (!_keepOpen)
                _sourceStream.Dispose();

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (WroteChunkCount > 0)
            {
                await _sourceStream.WriteAsync("0\r\n\r\n"u8.ToArray());
                await FlushAsync();
            }
            
            if (!_keepOpen)
                await _sourceStream.DisposeAsync();

            _disposed = true;
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
            var bytesRead = await _sourceStream.ReadAsync(_chunkHeaderBuffer, ++bufferOffset, 1, cancellationToken);
            if (bytesRead == 0) return 0; //no more chunk

            if (_chunkHeaderBuffer[bufferOffset] == '\r')
            {
                bytesRead = await _sourceStream.ReadAsync(_chunkHeaderBuffer, ++bufferOffset, 1, cancellationToken);
                if (bytesRead == 0) throw new InvalidDataException("Invalid HTTP Chunk header.");

                if (_chunkHeaderBuffer[bufferOffset] == '\n')
                    break;
            }

            // The chunk header buffer is already at its maximum size, throw an exception or handle the situation accordingly
            if (bufferOffset == _chunkHeaderBuffer.Length)
                throw new InvalidDataException("Chunk header exceeds the maximum size.");
        }

        var chunkSizeHex = Encoding.ASCII.GetString(_chunkHeaderBuffer, 0, bufferOffset - 1);
        if (!int.TryParse(chunkSizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize))
            throw new InvalidDataException("Invalid HTTP chunk size.");

        if (chunkSize != 0)
            ReadChunkCount++;

        return chunkSize;
    }

    private async Task ReadNextLine(CancellationToken cancellationToken)
    {
        var bytesRead = await _sourceStream.ReadAsync(_nextLineBuffer, 0, 2, cancellationToken);
        if (bytesRead < 2 || _nextLineBuffer[0] != '\r' || _nextLineBuffer[1] != '\n')
            throw new InvalidDataException("Invalid HTTP chunk.");
    }

    public override void Flush()
    {
        _writeBuffer.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _writeBuffer.FlushAsync(cancellationToken);
    }
}