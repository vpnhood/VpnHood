using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public class Socks5Client(Socks5Options options)
{
    public IPEndPoint ProxyEndPoint { get; } = options.ProxyEndPoint;
    private readonly string? _username = options.Username;
    private readonly string? _password = options.Password;
    private bool _isAuthenticated;

    /// <summary>
    /// Establish a TCP connection to the destination host via the SOCKS5 proxy (CONNECT).
    /// </summary>
    public async Task ConnectAsync(TcpClient tcpClient, IPEndPoint remoteEp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try {
            if (!tcpClient.Connected)
                await tcpClient.ConnectAsync(ProxyEndPoint, cancellationToken);

            var stream = tcpClient.GetStream();
            await EnsureAuthenticated(stream, cancellationToken);
            await PerformConnect(stream, remoteEp, cancellationToken);
        }
        catch {
            tcpClient.Close();
            throw;
        }
    }

    /// <summary>
    /// Perform a UDP ASSOCIATE using a dummy (0.0.0.0:0) client UDP endpoint.
    /// NOTE: Some proxies require the real client UDP port. Prefer the overload with clientUdpEndPoint.
    /// </summary>
    public Task<IPEndPoint> CreateUdpAssociateAsync(TcpClient tcpClient, CancellationToken cancellationToken) =>
        CreateUdpAssociateAsync(tcpClient, null, cancellationToken);

    /// <summary>
    /// Perform a UDP ASSOCIATE. If clientUdpEndPoint is provided, it is sent to the proxy so it knows
    /// which local UDP port we will use. This dramatically increases compatibility with strict proxies.
    /// </summary>
    public async Task<IPEndPoint> CreateUdpAssociateAsync(TcpClient tcpClient, IPEndPoint? clientUdpEndPoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!tcpClient.Connected)
            await tcpClient.ConnectAsync(ProxyEndPoint, cancellationToken);

        var stream = tcpClient.GetStream();
        await EnsureAuthenticated(stream, cancellationToken);

        var epToSend = clientUdpEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
        var result = await SendCommandAndReadReply(stream, Socks5Command.UdpAssociate, epToSend, cancellationToken);

        if (result.BoundAddress == null)
            throw new NotSupportedException("Proxy returned an unsupported address type for UDP ASSOCIATE.");

        // RFC: if BND.ADDR is 0.0.0.0 (or ::), use the proxy's address for sending datagrams.
        var addr = (result.BoundAddress.Equals(IPAddress.Any) || result.BoundAddress.Equals(IPAddress.IPv6Any))
            ? ProxyEndPoint.Address
            : result.BoundAddress;

        return new IPEndPoint(addr, result.BoundPort);
    }

    private async Task EnsureAuthenticated(NetworkStream stream, CancellationToken cancellationToken)
    {
        if (_isAuthenticated)
            return;

        await SelectAuth(stream, cancellationToken);
        _isAuthenticated = true;
    }

    private async Task SelectAuth(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[] {
            5,
            (byte)(_username != null ? 2 : 1), // number of methods
            (byte)Socks5AuthenticationType.NoAuthenticationRequired,
            (byte)Socks5AuthenticationType.UsernamePassword
        };

        // If no username/password, send only 1 method
        var length = _username != null ? buffer.Length : 2;
        await stream.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[2];
        await stream.ReadExactlyAsync(response, cancellationToken);
        var authMethod = (Socks5AuthenticationType)response[1];

        switch (authMethod) {
            case Socks5AuthenticationType.UsernamePassword:
                if (_username == null)
                    throw new UnauthorizedAccessException("Proxy requested username/password but none supplied.");
                await PerformUserNameAndPasswordAuth(stream, _username, _password ?? "", cancellationToken);
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
        await stream.WriteAsync(buffer, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[2];
        await stream.ReadExactlyAsync(response, cancellationToken);

        if (response[1] != 0)
            throw new UnauthorizedAccessException("Proxy authentication failed.");
    }

    private static async Task PerformConnect(NetworkStream stream, IPEndPoint destinationEndPoint,
        CancellationToken cancellationToken)
    {
        var result = await SendCommandAndReadReply(stream, Socks5Command.Connect, destinationEndPoint, cancellationToken);
        if (result.Reply != Socks5CommandReply.Succeeded)
            throw new SocketException((int)SocketError.NotConnected);
    }

    private static async Task<Socks5CommandResult> SendCommandAndReadReply(
        NetworkStream stream, Socks5Command command, IPEndPoint destinationEndPoint,
        CancellationToken cancellationToken)
    {
        var addressType = GetDestAddressType(destinationEndPoint.AddressFamily);
        var addressBytes = destinationEndPoint.Address.GetAddressBytes();
        var destPort = BuildDestPortBytes(destinationEndPoint.Port);

        var request = new byte[4 + addressBytes.Length + destPort.Length];
        request[0] = 5;
        request[1] = (byte)command;
        request[2] = 0;
        request[3] = addressType;
        addressBytes.CopyTo(request, 4);
        destPort.CopyTo(request, 4 + addressBytes.Length);

        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Reply: VER, REP, RSV, ATYP, BND.ADDR, BND.PORT
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        if (header[0] != 5)
            throw new IOException("Invalid SOCKS version in reply.");

        var reply = (Socks5CommandReply)header[1];
        if (reply != Socks5CommandReply.Succeeded) {
            await ReadAndDiscardAddressPort(stream, header[3], cancellationToken);
            HandleProxyCommandError([0, (byte)reply]);
        }

        var (boundAddress, boundPort) = await ReadAddressPort(stream, (Socks5AddressType)header[3], cancellationToken);
        return new Socks5CommandResult(command, reply, boundAddress, boundPort);
    }

    private static async Task ReadAndDiscardAddressPort(NetworkStream stream, byte addressType, CancellationToken ct)
    {
        var (addressLen, hasDomainLenByte) = addressType switch {
            (byte)Socks5AddressType.IpV4 => (4, false),
            (byte)Socks5AddressType.IpV6 => (16, false),
            (byte)Socks5AddressType.DomainName => (0, true),
            _ => throw new NotSupportedException("Unsupported ATYP in reply.")
        };

        if (hasDomainLenByte) {
            var lenBuf = new byte[1];
            await stream.ReadExactlyAsync(lenBuf, ct);
            addressLen = lenBuf[0];
        }
        var discard = new byte[addressLen + 2]; // address + port
        await stream.ReadExactlyAsync(discard, ct);
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
            Socks5CommandReply.CommandNotSupported => new NotSupportedException("Command not supported by SOCKS5 proxy."),
            Socks5CommandReply.AddressTypeNotSupported => new SocketException((int)SocketError.AddressFamilyNotSupported),
            _ => new SocketException((int)SocketError.ProtocolNotSupported)
        };

        throw exception;
    }

    private static byte GetDestAddressType(AddressFamily addressFamily) =>
        addressFamily switch {
            AddressFamily.InterNetwork => (byte)Socks5AddressType.IpV4,
            AddressFamily.InterNetworkV6 => (byte)Socks5AddressType.IpV6,
            _ => throw new NotSupportedException($"Unsupported address type: {addressFamily}.")
        };

    private static byte[] BuildDestPortBytes(int value) => [(byte)(value / 256), (byte)(value % 256)];

    private static Memory<byte> ConstructAuthBuffer(string username, string password)
    {
        var userBytes = Encoding.UTF8.GetBytes(username);
        var passBytes = Encoding.UTF8.GetBytes(password);
        // Ensure username and password are within limits
        if (userBytes.Length > 255 || passBytes.Length > 255)
            throw new ArgumentException("Username or password exceeds maximum length of 255 bytes.");

        var buffer = new byte[3 + userBytes.Length + passBytes.Length];
        buffer[0] = 0x01;
        buffer[1] = (byte)userBytes.Length;
        userBytes.CopyTo(buffer, 2);
        buffer[2 + userBytes.Length] = (byte)passBytes.Length;
        passBytes.CopyTo(buffer, 3 + userBytes.Length);
        return new Memory<byte>(buffer);
    }

    public static int WriteUdpRequest(Span<byte> destinationBuffer, IPEndPoint destination, ReadOnlySpan<byte> data)
    {
        var addressBytes = destination.Address.GetAddressBytes();
        var addressType = GetDestAddressType(destination.AddressFamily);
        var needed = 3 + 1 + addressBytes.Length + 2 + data.Length;
        if (destinationBuffer.Length < needed)
            throw new ArgumentException("Destination buffer is too small for the UDP request.", nameof(destinationBuffer));

        destinationBuffer[0] = 0;
        destinationBuffer[1] = 0;
        destinationBuffer[2] = 0;
        destinationBuffer[3] = addressType;
        var offset = 4;
        addressBytes.CopyTo(destinationBuffer[offset..]);
        offset += addressBytes.Length;
        destinationBuffer[offset++] = (byte)(destination.Port >> 8);
        destinationBuffer[offset++] = (byte)(destination.Port & 0xFF);
        data.CopyTo(destinationBuffer[offset..]);
        offset += data.Length;
        return offset;
    }

    private static async Task<(IPAddress? address, int port)> ReadAddressPort(Stream stream, Socks5AddressType addressType, CancellationToken ct)
    {
        if (addressType == Socks5AddressType.IpV4) {
            var buf = new byte[6];
            await stream.ReadExactlyAsync(buf, ct);
            return (new IPAddress(buf.AsSpan(0, 4)), (buf[4] << 8) | buf[5]);
        }

        if (addressType == Socks5AddressType.IpV6) {
            var buf = new byte[18];
            await stream.ReadExactlyAsync(buf, ct);
            return (new IPAddress(buf.AsSpan(0, 16)), (buf[16] << 8) | buf[17]);
        }

        if (addressType == Socks5AddressType.DomainName) {
            var lenBuf = new byte[1];
            await stream.ReadExactlyAsync(lenBuf, ct);
            var len = lenBuf[0];
            var buf = new byte[len + 2];
            await stream.ReadExactlyAsync(buf, ct);
            var port = (buf[len] << 8) | buf[len + 1];
            return (null, port);
        }

        throw new NotSupportedException("Unsupported AddressType in reply.");
    }

    /// <summary>
    /// Parse a SOCKS5 UDP response packet. Returns host when AddressType=DOMAIN, otherwise Address.
    /// </summary>
    public static Socks5UdpResponse ParseUdpResponse(ReadOnlySpan<byte> relayDatagram, out ReadOnlySpan<byte> payload)
    {
        if (relayDatagram.Length < 7)
            throw new ArgumentException("Packet too short for SOCKS5 UDP response.", nameof(relayDatagram));
        if (relayDatagram[0] != 0 || relayDatagram[1] != 0 || relayDatagram[2] != 0)
            throw new NotSupportedException("Fragmented or malformed SOCKS5 UDP packet.");

        var addressType = (Socks5AddressType)relayDatagram[3];
        var offset = 4;
        string? host = null;
        IPAddress? address = null;
        switch (addressType) {
            case Socks5AddressType.IpV4:
                address = new IPAddress(relayDatagram.Slice(offset, 4));
                offset += 4;
                break;

            case Socks5AddressType.IpV6:
                address = new IPAddress(relayDatagram.Slice(offset, 16));
                offset += 16;
                break;

            case Socks5AddressType.DomainName:
                var len = relayDatagram[offset];
                offset += 1;
                host = Encoding.ASCII.GetString(relayDatagram.Slice(offset, len));
                offset += len;
                break;
            default:
                throw new NotSupportedException("Unsupported AddressType in UDP response.");
        }

        var port = (relayDatagram[offset] << 8) | relayDatagram[offset + 1];
        offset += 2;
        payload = relayDatagram[offset..];
        return new Socks5UdpResponse(host, address, port);
    }
}