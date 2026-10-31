using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

internal sealed class Http3RecordingQuicStream : IQuicStream
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
    public void AbortRead(long errorCode) => ReadAbortCode = errorCode;
    public void AbortWrite(long errorCode) => WriteAbortCode = errorCode;
}
