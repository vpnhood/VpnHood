using System.Net;
using VpnHood.Net.Http3;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3AdmissionTest
{
    private static readonly Http3StreamContext Context = new(new IPEndPoint(IPAddress.Loopback, 443),
        new IPEndPoint(IPAddress.Loopback, 12345));

    public required TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(7)]
    [DataRow(13)]
    public async Task A_filter_cannot_delay_a_known_request_frame_waiting_for_more_bytes(int type)
    {
        byte[] wire = [(byte)type];
        using var source = new Http3AdmissionTestStream(wire);
        var filter = new Http3PendingStreamFilter();
        var admission = await Http3Admission.Read(source, Context, filter, 4096, TestContext.CancellationToken);
        await using var replay = admission.Stream;

        Assert.IsFalse(admission.Claimed);
        Assert.AreEqual(1, source.ReadCount, "The frame type must return to HTTP/3 before another read can wait.");
        await AssertReplay(replay, wire, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    public async Task A_fragmented_known_frame_type_returns_as_soon_as_its_integer_is_complete(int encodedLength)
    {
        var wire = new byte[encodedLength];
        wire[0] = encodedLength switch {
            2 => 0x40,
            4 => 0x80,
            8 => 0xc0,
            _ => throw new ArgumentOutOfRangeException(nameof(encodedLength))
        };
        wire[^1] = 3;
        using var source = new Http3AdmissionTestStream(wire);
        var filter = new Http3PendingStreamFilter();
        var admission = await Http3Admission.Read(source, Context, filter, 4096, TestContext.CancellationToken);
        await using var replay = admission.Stream;

        Assert.IsFalse(admission.Claimed);
        Assert.AreEqual(encodedLength, source.ReadCount);
        Assert.AreEqual(encodedLength, filter.LastPrefixLength);
        await AssertReplay(replay, wire, TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(64)]
    public async Task A_filter_stops_requesting_bytes_at_an_unknown_frames_payload_boundary(int payloadLength)
    {
        var frame = Http3Protocol.Frame(0x21, new byte[payloadLength]);
        byte[] wire = [.. frame, 0, 0];
        using var source = new Http3AdmissionTestStream(wire);
        var filter = new Http3PendingStreamFilter();
        var admission = await Http3Admission.Read(source, Context, filter, 4096, TestContext.CancellationToken);
        await using var replay = admission.Stream;

        Assert.IsFalse(admission.Claimed);
        Assert.AreEqual(frame.Length, source.ReadCount,
            "A following invalid DATA frame must remain available to the HTTP/3 parser.");
        Assert.AreEqual(frame.Length, filter.LastPrefixLength);
        await AssertReplay(replay, wire, TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task A_filter_cannot_wait_past_its_prefix_limit_inside_an_unknown_frame()
    {
        byte[] wire = [0x21, 0x40, 0x40, 1, 2, 3];
        using var source = new Http3AdmissionTestStream(wire);
        var filter = new Http3PendingStreamFilter();
        var admission = await Http3Admission.Read(source, Context, filter, 4, TestContext.CancellationToken);
        await using var replay = admission.Stream;

        Assert.IsFalse(admission.Claimed);
        Assert.AreEqual(4, source.ReadCount);
        Assert.AreEqual(4, filter.LastPrefixLength);
        await AssertReplay(replay, wire, TestContext.CancellationToken);
    }

    private static async Task AssertReplay(Stream stream, IReadOnlyList<byte> expected, CancellationToken cancellationToken)
    {
        var received = new byte[expected.Count];
        await stream.ReadExactlyAsync(received, cancellationToken);
        CollectionAssert.AreEqual(expected.ToArray(), received);
    }
}
