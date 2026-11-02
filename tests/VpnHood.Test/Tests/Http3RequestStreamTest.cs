using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

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
        using var source = new Http3EmptyDataFloodStream(headers);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        await using var reader = new Http3StreamReader(source, allowBufferGrowth: true, quic: quic);
        await Http3RequestStream.Process(reader, quic, static _ => { },
            new Http3RecordingResponder(), new Http3ServerOptions(),
            TestContext.CancellationToken,
            TestContext.CancellationToken);

        Assert.IsTrue(source.FloodBytesProvided <= 64 * 1024 + 4096,
            "A sender with no pauses must not extend inspection beyond its byte budget.");
        var response = output.ToArray();
        var offset = 0;
        Assert.AreEqual(1L, Http3Protocol.ReadInteger(response, ref offset));
        var length = Http3Protocol.ReadInteger(response, ref offset);
        using var responseStream = new MemoryStream(response, offset, (int)length);
        await using var responseReader = new Http3StreamReader(responseStream);
        var fields = await Http3Qpack.ReadSection(responseReader, length, new Http3ServerOptions(),
            TestContext.CancellationToken);
        Assert.AreEqual("401", fields.Fields.Single(x => x.Key == ":status").Value);
        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNull(quic.WriteAbortCode);
    }

    [TestMethod]
    [DataRow(100, "401", true)]
    [DataRow(3, "431", false)]
    public async Task Application_and_limit_responses_use_the_servers_header_policy(int maximumHeaderCount,
        string expectedStatus, bool responderCalled)
    {
        byte[] headers = [1, 18, 0, 0, 0xd1, 0xd7, 0xc1, 0x50, 11, .. "example.com"u8];
        using var source = new MemoryStream(headers);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        await using var reader = new Http3StreamReader(source, allowBufferGrowth: true, quic: quic);
        var responder = new Http3RecordingResponder();
        var options = new Http3ServerOptions {
            MaxHeaderCount = maximumHeaderCount,
            ResponseHeaders = status => [new("server", $"test-{status}"), new("content-length", "0")]
        };
        await Http3RequestStream.Process(reader, quic, static _ => { }, responder, options,
            TestContext.CancellationToken, TestContext.CancellationToken);

        output.Position = 0;
        await using var responseReader = new Http3StreamReader(output);
        Assert.AreEqual(1L, await responseReader.ReadInteger(TestContext.CancellationToken));
        var length = await responseReader.ReadInteger(TestContext.CancellationToken);
        var section = await Http3Qpack.ReadSection(responseReader, length, new Http3ServerOptions(),
            TestContext.CancellationToken);
        var fields = section.Fields.ToDictionary(x => x.Key, x => x.Value);
        Assert.AreEqual(expectedStatus, fields[":status"]);
        Assert.AreEqual($"test-{expectedStatus}", fields["server"]);
        Assert.AreEqual("0", fields["content-length"]);
        Assert.AreEqual(responderCalled, responder.Request != null);
        if (responderCalled) {
            Assert.IsNotNull(responder.Request);
            Assert.AreEqual("GET", responder.Request.Headers.Single(x => x.Key == ":method").Value);
            Assert.AreEqual("example.com", responder.Request.Headers.Single(x => x.Key == ":authority").Value);
        }
        Assert.AreEqual(1, quic.CompletedWrites);
        if (responderCalled)
            Assert.IsNull(quic.ReadAbortCode, "The parser consumed peer FIN on the complete request.");
        else
            Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
    }

    [TestMethod]
    public async Task Application_completion_cancels_and_joins_a_stalled_body_read()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] headers = [1, 18, 0, 0, 0xd1, 0xd7, 0xc1, 0x50, 11, .. "example.com"u8];
        var gate = new Http3RequestTestGate();
        using var source = new Http3GatedRequestStream(headers, [], gate);
        using var output = new MemoryStream();
        var quic = new Http3RecordingQuicStream(output);
        await using var reader = new Http3StreamReader(source, allowBufferGrowth: true, quic: quic);
        await Http3RequestStream.Process(reader, quic, error => Assert.Fail(error.ToString()),
            new Http3GatedResponder(gate), new Http3ServerOptions(),
            deadline.Token, deadline.Token);

        await reader.DisposeAsync();
        Assert.IsTrue(source.PendingReadEnded, "A canceled native read must end before its buffer can be released.");
        Assert.AreEqual(1, quic.CompletedWrites);
        Assert.AreEqual(Http3Protocol.NoError, quic.ReadAbortCode);
        Assert.IsNotEmpty(output.ToArray());
    }

}
