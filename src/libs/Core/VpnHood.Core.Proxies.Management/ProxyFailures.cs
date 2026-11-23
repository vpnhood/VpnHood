using System.Net.Sockets;
using VpnHood.Net.Proxies;

namespace VpnHood.Core.Proxies.Management;

internal static class ProxyFailures
{
    // A proxy's answer that the server it was asked for could not be reached: the proxy itself works. Not a failure to
    // reach the proxy, a SocketException, nor a refusal of the client, nor TLS with the server, which a proxy that
    // stands in for the server fails too
    public static bool IsServerUnreachable(Exception ex) =>
        ex is ProxyClientException {
            SocketErrorCode: SocketError.ConnectionRefused or SocketError.HostUnreachable or
            SocketError.NetworkUnreachable or SocketError.TimedOut
        };
}
