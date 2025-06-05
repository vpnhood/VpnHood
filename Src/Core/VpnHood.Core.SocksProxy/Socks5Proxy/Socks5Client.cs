using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public class Socks5Client(Socks5Options options)
{
    public IPEndPoint ProxyEndPoint { get; } = options.ProxyEndPoint;
    private readonly string? _username = options.Username;
    private readonly string? _password = options.Password;

    public async Task ConnectAsync(TcpClient tcpClient, IPEndPoint remoteEp, CancellationToken cancellationToken)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        try {
            await tcpClient.ConnectAsync(ProxyEndPoint, cancellationToken);
            var stream = tcpClient.GetStream();
            await SelectAuth(stream, cancellationToken);
            await PerformConnect(stream, remoteEp, cancellationToken);
        }
        catch {
            tcpClient.Close();
            throw;
        }
    }

    private async Task SelectAuth(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[] {
            5, // SOCKS version 5
            2, // Number of authentication methods supported
            (byte)Socks5AuthenticationType.NoAuthenticationRequired, // No authentication required
            (byte)Socks5AuthenticationType.UsernamePassword // Username/Password authentication
        };
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[2];
        await ReadExactAsync(stream, response, cancellationToken);
        var authMethod = (Socks5AuthenticationType)response[1];

        switch (authMethod) {
            case Socks5AuthenticationType.UsernamePassword:
                await PerformUserNameAndPasswordAuth(stream, _username ?? "", _password ?? "", cancellationToken);
                break;

            case Socks5AuthenticationType.NoAuthenticationRequired:
                break;

            case Socks5AuthenticationType.ReplyNoAcceptableMethods:
                throw new UnauthorizedAccessException("No acceptable authentication methods were found.");

            default:
                throw new UnauthorizedAccessException($"Authentication method {authMethod} is not supported.");
        }
    }

    private static async Task PerformUserNameAndPasswordAuth(NetworkStream stream, string userName, string password,
        CancellationToken cancellationToken)
    {
        var buffer = ConstructAuthBuffer(userName, password);
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[2];
        await ReadExactAsync(stream, response, cancellationToken);

        if (response[1] != 0)
            throw new UnauthorizedAccessException("Proxy authentication failed.");
    }

    private static async Task PerformConnect(NetworkStream stream, IPEndPoint destinationEndPoint,
        CancellationToken cancellationToken)
    {
        var addressType = GetDestAddressType(destinationEndPoint.AddressFamily);
        var addressBytes = destinationEndPoint.Address.GetAddressBytes();
        var destPort = GetDestPortBytes(destinationEndPoint.Port);

        var buffer = new byte[4 + addressBytes.Length + destPort.Length];
        buffer[0] = 5;
        buffer[1] = (byte)Socks5Command.Connect;
        buffer[2] = 0; //reserved
        buffer[3] = addressType;
        addressBytes.CopyTo(buffer, 4);
        destPort.CopyTo(buffer, 4 + addressBytes.Length);

        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[10];
        await ReadExactAsync(stream, response, cancellationToken);

        if (response[1] != (byte)Socks5CommandReply.Succeeded)
            HandleProxyCommandError(response);
    }

    private static void HandleProxyCommandError(byte[] response)
    {
        var replyCode = (Socks5CommandReply)response[1];
        Exception exception = replyCode switch {
            Socks5CommandReply.GeneralSocksServerFailure => new SocketException((int)SocketError.SocketError),
            Socks5CommandReply.ConnectionNotAllowedByRuleset => new SocketException((int)SocketError.AccessDenied),
            Socks5CommandReply.NetworkUnreachable => new SocketException((int)SocketError.NetworkUnreachable),
            Socks5CommandReply.HostUnreachable => new SocketException((int)SocketError.HostUnreachable),
            Socks5CommandReply.ConnectionRefused => new SocketException((int)SocketError.ConnectionRefused),
            Socks5CommandReply.TtlExpired => new IOException("TTL Expired."),
            Socks5CommandReply.CommandNotSupported => new NotSupportedException(
                "Command not supported by SOCKS5 proxy."),
            Socks5CommandReply.AddressTypeNotSupported => new SocketException(
                (int)SocketError.AddressFamilyNotSupported),
            _ => new SocketException((int)SocketError.ProtocolNotSupported)
        };

        throw exception;
    }

    private static byte GetDestAddressType(AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork => (byte)Socks5AddressType.IpV4,
            AddressFamily.InterNetworkV6 => (byte)Socks5AddressType.IpV6,
            _ => throw new NotSupportedException($"Unsupported address type: {addressFamily}.")
        };
    }

    public static byte[] GetDestPortBytes(int value)
    {
        return [(byte)(value / 256), (byte)(value % 256)];
    }

    public static byte[] ConstructAuthBuffer(string username, string password)
    {
        var userBytes = Encoding.ASCII.GetBytes(username);
        var passBytes = Encoding.ASCII.GetBytes(password);
        var buffer = new byte[3 + userBytes.Length + passBytes.Length];

        buffer[0] = 0x01;
        buffer[1] = (byte)userBytes.Length;
        userBytes.CopyTo(buffer, 2);
        buffer[2 + userBytes.Length] = (byte)passBytes.Length;
        passBytes.CopyTo(buffer, 3 + userBytes.Length);

        return buffer;
    }

    public static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length) {
            var bytesRead = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken);
            if (bytesRead == 0)
                throw new IOException("Unexpected end of stream.");

            read += bytesRead;
        }
    }
}