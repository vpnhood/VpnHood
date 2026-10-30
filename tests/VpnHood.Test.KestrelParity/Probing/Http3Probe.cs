using System.Net;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Test.KestrelParity.Probing;

// Errors are observations, but a deadline is a failed probe. Comparing two timeouts would hide a stalled front.
internal sealed class Http3Probe(string hostName, X509Certificate2 certificate)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public ValueTask<QuicConnection> Connect(IPEndPoint endPoint, int incomingOneWayStreams,
        CancellationToken cancellationToken) => QuicConnection.ConnectAsync(new QuicClientConnectionOptions {
        RemoteEndPoint = endPoint,
        MaxInboundUnidirectionalStreams = incomingOneWayStreams,
        DefaultCloseErrorCode = 0x100,
        DefaultStreamErrorCode = 0x10c,
        HandshakeTimeout = Timeout,
        IdleTimeout = System.Threading.Timeout.InfiniteTimeSpan,
        // Exercise the server's HTTP deadline, not the transport's idle timeout while a probe withholds data.
        KeepAliveInterval = TimeSpan.FromSeconds(5),
        ClientAuthenticationOptions = new SslClientAuthenticationOptions {
            TargetHost = hostName,
            ApplicationProtocols = [SslApplicationProtocol.Http3],
            RemoteCertificateValidationCallback = (_, presented, _, _) =>
                presented?.GetCertHashString() == certificate.GetCertHashString()
        }
    }, cancellationToken);

    public async Task<Http3ProbeResult> Request(IPEndPoint endPoint, byte[] bytes, bool fragmented,
        bool withholdFin, bool sendFin, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        await using var connection = await Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        return await Request(request, bytes, fragmented, withholdFin, sendFin, deadline.Token);
    }

    public static async Task<Http3ProbeResult> Request(QuicStream request, byte[] bytes, bool fragmented,
        bool withholdFin, bool sendFin, CancellationToken cancellationToken)
    {
        // Fragment the first request bytes and the proof as a real network can. No delay is inserted per byte.
        var fragmentLength = fragmented ? 7 : bytes.Length;
        try {
            for (var offset = 0; offset < bytes.Length; offset += fragmentLength)
                await request.WriteAsync(bytes.AsMemory(offset, Math.Min(fragmentLength, bytes.Length - offset)),
                    cancellationToken);
        }
        catch (QuicException) {
            // A malformed request may be rejected before the final write; still observe its read-side result.
        }

        var result = ReadResult(request, cancellationToken);
        if (withholdFin) {
            // These bytes form an unfinished unknown HTTP/3 frame, so neither side may answer yet. Completing
            // the stream afterward must produce a measured result; this delay alone never makes the test pass.
            var first = await Task.WhenAny(result, Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken));
            Assert.AreNotSame(result, first, "The server answered an incomplete raw HTTP/1 request before FIN.");
        }

        if (sendFin) {
            try {
                request.CompleteWrites();
            }
            catch (QuicException) {
                // The peer may already have rejected the frame.
            }
        }
        return await result;
    }

    public async Task<Http3ProbeResult> Control(IPEndPoint endPoint, byte[] bytes, bool duplicate, bool finish,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        await using var connection = await Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(bytes, deadline.Token);
        if (finish)
            control.CompleteWrites();

        QuicStream? duplicateControl = null;
        var serverStreams = new List<QuicStream>();
        try {
            if (duplicate) {
                duplicateControl = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
                await duplicateControl.WriteAsync(bytes, deadline.Token);
            }

            try {
                while (true) {
                    // Keep the server's critical streams open. Disposing one here would cause an unrelated
                    // STOP_SENDING, and could let a wrong implementation pass by reporting that error instead.
                    serverStreams.Add(await connection.AcceptInboundStreamAsync(deadline.Token));
                }
            }
            catch (QuicException ex) {
                return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
            }
        }
        finally {
            if (duplicateControl != null)
                await duplicateControl.DisposeAsync();
            foreach (var stream in serverStreams)
                await stream.DisposeAsync();
        }
    }

    public Task<byte[]> Response(IPEndPoint endPoint, byte[] bytes, byte[] controlSuffix,
        byte[]? extraOneWayBytes, CancellationToken cancellationToken) =>
        Response(endPoint, bytes, controlSuffix, extraOneWayBytes, null, cancellationToken);

    public async Task<byte[]> Response(IPEndPoint endPoint, byte[] bytes, byte[] controlSuffix,
        byte[]? extraOneWayBytes, byte[]? initialControlBytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        await using var connection = await Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(initialControlBytes ?? new byte[] { 0, 4, 0 }, deadline.Token);
        if (controlSuffix.Length != 0)
            await control.WriteAsync(controlSuffix, deadline.Token);

        QuicStream? extraOneWay = null;
        try {
            if (extraOneWayBytes != null) {
                extraOneWay = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
                try {
                    await extraOneWay.WriteAsync(extraOneWayBytes, deadline.Token);
                    extraOneWay.CompleteWrites();
                }
                catch (QuicException ex) when (ex.QuicError == QuicError.StreamAborted) {
                    // An unknown one-way stream may be stopped. The valid request still has to finish.
                }
            }

            await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional,
                deadline.Token);
            await request.WriteAsync(bytes, deadline.Token);
            request.CompleteWrites();
            using var response = new MemoryStream();
            var buffer = new byte[4096];
            while (true) {
                var count = await request.ReadAsync(buffer, deadline.Token);
                if (count == 0)
                    return response.ToArray();
                Assert.IsTrue(response.Length + count <= 4096, "Unexpectedly large empty challenge response.");
                response.Write(buffer, 0, count);
            }
        }
        finally {
            if (extraOneWay != null)
                await extraOneWay.DisposeAsync();
        }
    }

    public async Task<(Http3ProbeResult Result, TimeSpan Elapsed)> PartialRequest(IPEndPoint endPoint,
        TimeSpan headerTimeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(headerTimeout + TimeSpan.FromSeconds(10));
        await using var connection = await Connect(endPoint, 3, deadline.Token);
        await using var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
        await control.WriteAsync(new byte[] { 0, 4, 0 }, deadline.Token);
        await using var request = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, deadline.Token);
        var started = Stopwatch.GetTimestamp();
        await request.WriteAsync("GET "u8.ToArray(), deadline.Token);
        // Leave both the HTTP/1 candidate and its HTTP/3 unknown frame unfinished. Only the server's header
        // timeout should end this request; our longer deadline failing means no protocol result was measured.
        var result = await ReadResult(request, deadline.Token);
        return (result, Stopwatch.GetElapsedTime(started));
    }

    private static async Task<Http3ProbeResult> ReadResult(QuicStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            return new Http3ProbeResult(Convert.ToHexString(buffer.AsSpan(0, count)), count == 0, null, null);
        }
        catch (QuicException ex) {
            return new Http3ProbeResult("", false, ex.QuicError, ex.ApplicationErrorCode);
        }
    }
}
