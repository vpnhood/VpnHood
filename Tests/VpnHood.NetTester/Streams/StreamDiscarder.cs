namespace VpnHood.NetTester.Streams;

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

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}