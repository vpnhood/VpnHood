using System.Net;
using System.Net.Quic;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Framing")]
    [DataRow("before-headers", 70 * 1024, true)]
    [DataRow("after-headers", 70 * 1024, true)]
    [DataRow("declared-after-headers", 1024 * 1024, false)]
    public async Task Large_unknown_request_frames_preserve_the_reference_response_and_connection(
        string position, int declaredLength, bool completePayload)
    {
        var extension = BuildUnknownRequestFrame(declaredLength, completePayload);
        var headers = BuildQpackRequest([]);
        byte[] bytes = position == "before-headers" ? [.. extension, .. headers] : [.. headers, .. extension];
        var expected = await ObserveUnknownRequestFrame(Servers.ReferenceEndPoint, bytes, completePayload,
            TestContext.CancellationToken);
        var actual = await ObserveUnknownRequestFrame(Servers.OurEndPoint, bytes, completePayload,
            TestContext.CancellationToken);

        Assert.IsTrue(expected.Response.Fin, "The reference must finish its challenge.");
        Assert.IsNotEmpty(expected.Response.Bytes);
        AssertSame(position + " response", expected.Response, actual.Response);
        Assert.IsTrue(expected.Following.Fin, "The reference must keep the connection usable.");
        Assert.IsNotEmpty(expected.Following.Bytes);
        AssertSame(position + " following request", expected.Following, actual.Following);
    }

    private static byte[] BuildUnknownRequestFrame(int declaredLength, bool completePayload)
    {
        using var frame = new MemoryStream();
        WriteVariableInteger(frame, 0x21);
        WriteVariableInteger(frame, (ulong)declaredLength);
        // A payload byte dispatches the frame even when the rest of its declared megabyte is withheld.
        frame.Write(new byte[completePayload ? declaredLength : 1]);
        return frame.ToArray();
    }

    private static async Task<(Http3ProbeResult Response, Http3ProbeResult Following)> ObserveUnknownRequestFrame(
        IPEndPoint endPoint, byte[] bytes, bool finish, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Http3Probe.Timeout);
        await using var connection = await CreateProbe().Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        await SendRequestTail(request, bytes, finish, deadline.Token);
        var response = await ReadCompleteResponse(request, deadline.Token);
        // A close after the first response is still observable through a second request on this connection.
        var following = await ReadFollowingUnknownFrameRequest(connection, deadline.Token);
        return (response, following);
    }

    private static async Task<Http3ProbeResult> ReadFollowingUnknownFrameRequest(QuicConnection connection,
        CancellationToken cancellationToken)
    {
        try {
            await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional,
                cancellationToken);
            await SendRequestTail(request, BuildQpackRequest([]), finish: true, cancellationToken);
            return await ReadCompleteResponse(request, cancellationToken);
        }
        catch (QuicException ex) {
            return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
        }
    }
}
