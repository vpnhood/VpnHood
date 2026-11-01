using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

internal sealed class Http3RecordingQuicStream(Stream output) : IQuicStream
{
    public long StreamId => 0;
    public bool CanRead => true;
    public bool CanWrite => true;
    public Task ReadsClosed => Task.CompletedTask;
    public Task WritesClosed => Task.CompletedTask;
    public int CompletedWrites { get; private set; }
    public long? ReadAbortCode { get; private set; }
    public long? WriteAbortCode { get; private set; }

    public void CompleteWrites() => CompletedWrites++;
    public ValueTask WriteFinal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var write = output.WriteAsync(buffer, cancellationToken);
        CompleteWrites();
        return write;
    }
    public void AbortRead(long errorCode) => ReadAbortCode = errorCode;
    public void AbortWrite(long errorCode) => WriteAbortCode = errorCode;
}
