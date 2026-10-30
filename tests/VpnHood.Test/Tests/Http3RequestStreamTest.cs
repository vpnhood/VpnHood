using VpnHood.Core.Server.Listeners.Http3;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3RequestStreamTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task A_continuously_readable_empty_data_flood_still_receives_the_final_challenge()
    {
        // Static GET/https/path plus a literal authority. Subsequent zero bytes form empty DATA frames.
        byte[] headers = [1, 18, 0, 0, 0xd1, 0xd7, 0xc1, 0x50, 11, .. "example.com"u8];
        using var source = new EmptyDataFloodStream(headers);
        using var output = new MemoryStream();
        await using var reader = new Http3StreamReader(source);
        var quic = new RecordingQuicStream();
        await Http3RequestStream.Process(output, reader, quic, TestContext.CancellationToken,
            TestContext.CancellationToken);

        Assert.IsTrue(source.FloodBytesProvided <= 64 * 1024 + 4096,
            "A sender with no pauses must not extend inspection beyond its byte budget.");
        var response = output.ToArray();
        var offset = 0;
        Assert.AreEqual(1L, Http3Protocol.ReadInteger(response, ref offset));
        var length = Http3Protocol.ReadInteger(response, ref offset);
        var fields = Http3Qpack.Decode(response.AsSpan(offset, (int)length));
        Assert.AreEqual("401", fields.Single(x => x.Key == ":status").Value);
        Assert.IsTrue(quic.WritesCompleted);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNull(quic.WriteAbortCode);
    }

    private sealed class EmptyDataFloodStream(byte[] prefix) : MemoryStream(prefix)
    {
        public int FloodBytesProvided { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Position < Length)
                return base.ReadAsync(buffer, cancellationToken);
            // This guard makes a regression fail promptly instead of hanging the test or allocating
            // a large body. The source itself stores only the request head and reuses the read buffer.
            if (FloodBytesProvided >= 128 * 1024)
                throw new InvalidOperationException("The available-body inspection did not stop.");
            buffer.Span.Clear();
            FloodBytesProvided += buffer.Length;
            return new ValueTask<int>(buffer.Length);
        }
    }

    private sealed class RecordingQuicStream : IQuicStream
    {
        public long StreamId => 0;
        public bool CanRead => true;
        public bool CanWrite => true;
        public Task ReadsClosed => Task.CompletedTask;
        public Task WritesClosed => Task.CompletedTask;
        public bool WritesCompleted { get; private set; }
        public long? ReadAbortCode { get; private set; }
        public long? WriteAbortCode { get; private set; }
        public void CompleteWrites() => WritesCompleted = true;
        public void AbortRead(long errorCode) => ReadAbortCode = errorCode;
        public void AbortWrite(long errorCode) => WriteAbortCode = errorCode;
    }
}
