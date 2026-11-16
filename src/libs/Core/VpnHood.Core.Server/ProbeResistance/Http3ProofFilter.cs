using System.Runtime.InteropServices;
using VpnHood.Net.Http3;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// Compatibility (2026-10): clients carry HTTP/1 inside QUIC until the tunnel moves to standard HTTP/3.
internal sealed class Http3ProofFilter(SessionManager sessionManager) : IHttp3StreamFilter
{
    public async Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CouldBeClientRequest(prefix.Span))
            return Http3StreamDecision.NotClaimed;

        var end = prefix.Span.IndexOf("\r\n\r\n"u8);
        if (end < 0)
            return Http3StreamDecision.NeedMore;

        return await Authenticate(prefix[..(end + 4)], cancellationToken).Vhc()
            ? Http3StreamDecision.Claimed
            : Http3StreamDecision.NotClaimed;
    }

    private static bool CouldBeClientRequest(ReadOnlySpan<byte> bytes) =>
        "GET "u8.StartsWith(bytes[..Math.Min(bytes.Length, 4)]) ||
        "POST "u8.StartsWith(bytes[..Math.Min(bytes.Length, 5)]);

    private async Task<bool> Authenticate(ReadOnlyMemory<byte> prefix, CancellationToken cancellationToken)
    {
        using var memory = OpenPrefix(prefix);
        await using var reader = new HttpReadStream(memory);
        try {
            var request = await HttpRequestHeadReader.Read(reader, cancellationToken).Vhc();
            var minimum = Math.Max(15, sessionManager.SessionOptions.MinClientProtocolVersion ?? 0);
            return ClientRequestAuthenticator.TryAuthenticate(request, sessionManager.RequestProof,
                minimum, ServerHost.MaxProtocolVersion, out _);
        }
        catch (HttpRequestRejectedException) {
            return false;
        }
        catch (EndOfStreamException) {
            return false;
        }
    }

    private static MemoryStream OpenPrefix(ReadOnlyMemory<byte> prefix) =>
        MemoryMarshal.TryGetArray(prefix, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(prefix.ToArray(), writable: false);
}
