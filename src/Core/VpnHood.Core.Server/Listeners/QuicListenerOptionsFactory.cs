using System.Net;
using VpnHood.Net.Http3;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Core.Server.Listeners;

internal static class QuicListenerOptionsFactory
{
    public static QuicListenerOptions Create(IPEndPoint endPoint, IQuicCertificateSelector certificateSelector) => new() {
        ListenEndPoint = endPoint,
        // HTTP/3 owns idle and request deadlines; native expiry must not end a busy tunnel.
        IdleTimeout = Timeout.InfiniteTimeSpan,
        DefaultStreamErrorCode = Http3ErrorCode.InternalError,
        DefaultCloseErrorCode = Http3ErrorCode.NoError,
        ServerCertificateSelector = certificateSelector.SelectCertificate
    };
}
