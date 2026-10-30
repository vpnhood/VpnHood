using System.Buffers;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Listeners.Http3;

// Compatibility (2026-10): existing clients carry HTTP/1 inside QUIC. Only a verified protocol-15
// proof admits that path; remove this gate when the tunnel itself migrates to standard HTTP/3.
internal static class Http3Admission
{
    private const int PrefixLimit = 4096;

    public static async Task<(Stream Stream, bool Authenticated)> Read(Stream source, SessionManager sessionManager,
        CancellationToken cancellationToken)
    {
        var prefix = ArrayPool<byte>.Shared.Rent(PrefixLimit);
        var count = 0;
        var handedOff = false;
        try {
            var authenticated = false;
            while (count < PrefixLimit) {
                var read = await source.ReadAsync(prefix.AsMemory(count, PrefixLimit - count), cancellationToken).Vhc();
                if (read == 0)
                    break;
                count += read;
                if (!CouldBeClientRequest(prefix.AsSpan(0, count)))
                    break;
                var end = prefix.AsSpan(0, count).IndexOf("\r\n\r\n"u8);
                if (end < 0)
                    continue;
                authenticated = await Authenticate(prefix, end + 4, sessionManager, cancellationToken).Vhc();
                break;
            }
            // GET/POST also look like unknown HTTP/3 frames. Decide before their first payload ends
            // (5156/4952 bytes), so waiting for HTTP/1 headers cannot hide the next HTTP/3 frame error.
            var replay = new Http3PrefixStream(source, prefix, count);
            handedOff = true;
            return (replay, authenticated);
        }
        finally {
            if (!handedOff)
                ArrayPool<byte>.Shared.Return(prefix, clearArray: true);
        }
    }

    private static bool CouldBeClientRequest(ReadOnlySpan<byte> bytes) =>
        "GET "u8.StartsWith(bytes[..Math.Min(bytes.Length, 4)]) ||
        "POST "u8.StartsWith(bytes[..Math.Min(bytes.Length, 5)]);

    private static async Task<bool> Authenticate(byte[] bytes, int length, SessionManager sessionManager,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(bytes, 0, length, writable: false);
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
}
