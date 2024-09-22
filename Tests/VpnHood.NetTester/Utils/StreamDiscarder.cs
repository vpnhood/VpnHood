namespace VpnHood.NetTester.Utils;

public class StreamDiscarder(Speedometer? speedometer) : Stream
{
    // Override the required members of Stream

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true; // We want to allow writing, but discard the data

    public override long Length => throw new NotSupportedException();

    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // The main functionality: discard data by doing nothing
    public override void Write(byte[] buffer, int offset, int count)
    {
        // Do nothing, effectively discarding the data
        speedometer?.AddWrite(count);
    }

    public async Task ReadFromAsync(Stream source, long size, int bufferSize = 81920,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[bufferSize];
        var totalBytesCopied = 0;

        while (totalBytesCopied < size) {
            var bytesToRead = (int)Math.Min(bufferSize, size - totalBytesCopied);
            var bytesRead = await source.ReadAsync(buffer, 0, bytesToRead, cancellationToken);
            if (bytesRead == 0)
                break; // End of source stream

            await WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalBytesCopied += bytesRead;
        }
    }


    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}