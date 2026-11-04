using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3TrailerScheduleTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task Application_completion_ignores_a_withheld_hard_overrun()
    {
        var result = await Observe(CreateHeaders(), Http3Protocol.Frame(1, CreateTrailerBlock(5)),
            new Http3ServerOptions { MaxHeaderCount = 4 }, TestContext.CancellationToken,
            holdTailUntilResponse: true);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(0, result.TailBytesProvided);
    }

    [TestMethod]
    public async Task Selection_is_frozen_while_later_fields_cross_the_soft_limit()
    {
        var result = await Observe(CreateHeaders(), Http3Protocol.Frame(1, CreateTrailerBlock(3)),
            new Http3ServerOptions { MaxHeaderCount = 4 }, TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.IsTrue(result.PendingReadEnded);
    }

    [TestMethod]
    [DataRow(false, 0x10eL)]
    [DataRow(true, 0x10bL)]
    public async Task A_hard_overrun_before_emission_resets_without_a_response(bool octets, long errorCode)
    {
        byte[] trailers = octets ? [0, 0, 0x21, (byte)'x', 52, .. Enumerable.Repeat((byte)'a', 52)] :
            CreateTrailerBlock(5);
        var options = octets ? new Http3ServerOptions { MaxHeaderBytes = 50 } :
            new Http3ServerOptions { MaxHeaderCount = 4 };
        var result = await Observe(CreateHeaders(), Http3Protocol.Frame(1, trailers), options,
            TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual(false, result.ConnectionError);
        Assert.AreEqual(0, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Pending_input_without_trailers_does_not_delay_the_response()
    {
        var result = await Observe(CreateHeaders(), [], new Http3ServerOptions(), TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.IsTrue(result.PendingReadEnded);
    }

    [TestMethod]
    public async Task Pending_input_inside_a_trailer_does_not_delay_the_response()
    {
        var trailers = Http3Protocol.Frame(1, CreateTrailerBlock(3));
        var result = await Observe(CreateHeaders(), trailers[..^3],
            new Http3ServerOptions { MaxHeaderCount = 4 }, TestContext.CancellationToken);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.IsTrue(result.PendingReadEnded);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task The_inspection_budget_bounds_trailers_and_later_extension_payloads(bool afterTrailers)
    {
        byte[] tail = afterTrailers
            ? [.. Http3Protocol.Frame(1, CreateTrailerBlock(1)), .. Http3Protocol.Frame(64, new byte[300]),
                .. Http3Protocol.Frame(0, [1])]
            : Http3Protocol.Frame(1, CreateTrailerBlock(5));
        var budget = afterTrailers ? 20 : 10;
        var result = await Observe(CreateHeaders(), tail,
            new Http3ServerOptions { MaxHeaderCount = 4, MaxBodyInspectionBytes = budget },
            TestContext.CancellationToken, responseReleaseBytes: budget, maximumRead: 2);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(budget, result.TailBytesProvided);
    }

    [TestMethod]
    public async Task Data_before_trailers_keeps_its_content_length_validation()
    {
        byte[] tail = [.. Http3Protocol.Frame(0, [1]), .. Http3Protocol.Frame(1, CreateTrailerBlock(5))];
        var result = await Observe(CreateHeaders(zeroContentLength: true), tail,
            new Http3ServerOptions(), TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(Http3Protocol.ProtocolError, result.ErrorCode);
        Assert.AreEqual(false, result.ConnectionError);
        Assert.AreEqual(0, result.CompletedWrites);
    }

    [TestMethod]
    [DataRow(0L, false)]
    [DataRow(1L, false)]
    [DataRow(0L, true)]
    public async Task Buffered_forbidden_frames_after_trailers_still_close_the_connection(long type, bool extension)
    {
        byte[] tail = extension
            ? [.. Http3Protocol.Frame(1, CreateTrailerBlock(1)), .. Http3Protocol.Frame(64, new byte[300]),
                .. Http3Protocol.Frame(type, [1])]
            : [.. Http3Protocol.Frame(1, CreateTrailerBlock(1)), .. Http3Protocol.Frame(type, [1])];
        var result = await Observe(CreateHeaders(), tail, new Http3ServerOptions(), TestContext.CancellationToken);

        Assert.IsNull(result.Status);
        Assert.AreEqual(Http3Protocol.FrameUnexpected, result.ErrorCode);
        Assert.AreEqual(true, result.ConnectionError);
        Assert.AreEqual(0, result.CompletedWrites);
    }

    [TestMethod]
    public async Task Application_completion_before_a_following_frame_keeps_the_response()
    {
        byte[] buffered = [.. CreateHeaders(), .. Http3Protocol.Frame(1, CreateTrailerBlock(1))];
        var result = await Observe(buffered, Http3Protocol.Frame(0, [1]), new Http3ServerOptions(), TestContext.CancellationToken,
            holdTailUntilResponse: true);

        Assert.AreEqual("401", result.Status);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(1, result.CompletedWrites);
        Assert.AreEqual(0, result.TailBytesProvided);
    }

    private static async Task<(string? Status, long? ErrorCode, int CompletedWrites,
        bool? ConnectionError, int TailBytesProvided, bool PendingReadEnded)> Observe(
        byte[] headers, byte[] tail, Http3ServerOptions options, CancellationToken cancellationToken,
        bool holdTailUntilResponse = false, int responseReleaseBytes = int.MaxValue, int maximumRead = int.MaxValue)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var gate = new Http3RequestTestGate();
        await using var source = new Http3GatedRequestStream(headers, tail, gate, holdTailUntilResponse,
            responseReleaseBytes, maximumRead);
        await using var output = new Http3SignaledOutputStream(gate);
        var quic = new Http3RecordingQuicStream(output);
        await using var reader = new Http3StreamReader(source, allowBufferGrowth: true, quic: quic);
        var errors = new List<Exception>();
        Http3ProtocolException? failure = null;
        try {
            await Http3RequestStream.Process(reader, quic, errors.Add,
                new Http3GatedResponder(gate, waitForInput: !holdTailUntilResponse), options,
                deadline.Token, deadline.Token);
        }
        catch (Http3ProtocolException ex) {
            failure = ex;
        }
        if (failure != null)
            Assert.AreSame(failure, errors.Single());
        else
            Assert.IsEmpty(errors);
        var status = output.Length == 0 ? null : await ReadStatus(output.ToArray(), deadline.Token);
        return (status, failure?.ErrorCode, quic.CompletedWrites, failure?.ConnectionError,
            source.TailBytesProvided, source.PendingReadEnded);
    }

    private static async Task<string> ReadStatus(byte[] response, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(response);
        await using var reader = new Http3StreamReader(source);
        Assert.AreEqual(1L, await reader.ReadInteger(cancellationToken));
        var length = await reader.ReadInteger(cancellationToken);
        var section = await Http3Qpack.ReadSection(reader, length, new Http3ServerOptions(), cancellationToken);
        return section.Fields.Single(field => field.Key == ":status").Value;
    }

    private static byte[] CreateHeaders(bool zeroContentLength = false)
    {
        byte[] fields = [0, 0, 0xd1, 0xd7, 0xc1, 0x50, 11, .. "example.com"u8];
        byte[] block = zeroContentLength ? [.. fields, 0xc4] : fields;
        return Http3Protocol.Frame(1, block);
    }

    private static byte[] CreateTrailerBlock(int fields)
    {
        var bytes = new List<byte> { 0, 0 };
        for (var i = 0; i < fields; i++)
            bytes.AddRange([0x21, (byte)'x', 0]);
        return bytes.ToArray();
    }
}
