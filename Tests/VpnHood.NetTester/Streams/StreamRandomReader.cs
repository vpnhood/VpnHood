namespace VpnHood.NetTester.Streams;

// Custom stream to generate random data chunk by chunk
public class StreamRandomReader(long length, Speedometer? speedometer) : Stream
{
    private long _position;
    private readonly Random _random = new();
    private long _length = length;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override void SetLength(long value) => _length = value;
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };
        return _position;
    }


    public override long Position {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length)
            return 0; // EOF

        // Calculate how much data to read based on the remaining size
        var bytesToRead = (int)Math.Min(count, _length - _position);

        // Fill the buffer with random data
        _random.NextBytes(buffer.AsSpan(offset, bytesToRead));

        _position += bytesToRead;
        speedometer?.AddWrite(bytesToRead);
        return bytesToRead;
    }

}