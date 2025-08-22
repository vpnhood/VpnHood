using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.SocksProxy.Socks4ProxyClients;

// Minimal SOCKS4/4a client supporting CONNECT only
public class Socks4ProxyClient(Socks4ProxyClientOptions options) : IProxyClient
{
    public async Task ConnectAsync(TcpClient tcpClient, IPEndPoint destination, CancellationToken cancellationToken)
        => await ConnectAsync(tcpClient, destination.Address.ToString(), destination.Port, cancellationToken).ConfigureAwait(false);

    public async Task ConnectAsync(TcpClient tcpClient, string host, int port, CancellationToken cancellationToken)
    {
        try {
            if (!tcpClient.Connected)
                await tcpClient.ConnectAsync(options.ProxyEndPoint, cancellationToken).ConfigureAwait(false);

            var stream = tcpClient.GetStream();
            var isIp = IPAddress.TryParse(host, out var ipAddress) && ipAddress.AddressFamily == AddressFamily.InterNetwork;
            if (!isIp) 
                ipAddress = IPAddress.Parse("0.0.0.1");

            var request = BuildRequest(ipAddress!, port, isIp ? null : host, options.UserName);
            await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var reply = new byte[8];
            await stream.ReadExactlyAsync(reply, cancellationToken).ConfigureAwait(false);
            var code = (Socks4ReplyCode)reply[1];
            if (code != Socks4ReplyCode.RequestGranted)
                ThrowSocks4Error(code);
        }
        catch {
            tcpClient.Close();
            throw;
        }
    }

    private static ReadOnlyMemory<byte> BuildRequest(IPAddress ip, int port, string? domainName, string? userId)
    {
        if (ip.IsV6())
            throw new ProxyClientException(SocketError.OperationNotSupported, "SOCKS4 only supports IPv4 addresses.");

        var user = userId ?? string.Empty;
        var userBytes = Encoding.ASCII.GetBytes(user);
        var hostBytes = domainName != null ? Encoding.UTF8.GetBytes(domainName) : null;
        var addressBytes = ip.GetAddressBytes();
        var buffer = new byte[1 + 1 + 2 + 4 + userBytes.Length + 1 + (hostBytes?.Length ?? 0) + (domainName != null ? 1 : 0)];
        var o = 0;
        buffer[o++] = 0x04; // VN
        buffer[o++] = 0x01; // CD=CONNECT
        buffer[o++] = (byte)(port >> 8);
        buffer[o++] = (byte)(port & 0xFF);
        Array.Copy(addressBytes, 0, buffer, o, 4); o += 4;
        if (userBytes.Length > 0) Array.Copy(userBytes, 0, buffer, o, userBytes.Length);
        o += userBytes.Length;
        buffer[o++] = 0x00; // user id null terminator
        if (hostBytes != null) {
            Array.Copy(hostBytes, 0, buffer, o, hostBytes.Length);
            o += hostBytes.Length;
            buffer[o++] = 0x00; // host null terminator (SOCKS4a)
        }
        return new ReadOnlyMemory<byte>(buffer, 0, o);
    }

    private static void ThrowSocks4Error(Socks4ReplyCode code)
    {
        throw new ProxyClientException(code switch {
            Socks4ReplyCode.RequestRejectedOrFailed => SocketError.AccessDenied,
            Socks4ReplyCode.CannotConnectToIdentd => SocketError.ConnectionRefused,
            Socks4ReplyCode.DifferingUserId => SocketError.AccessDenied,
            _ => SocketError.SocketError
        });
    }
}
