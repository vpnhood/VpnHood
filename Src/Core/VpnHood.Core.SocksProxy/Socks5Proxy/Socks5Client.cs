using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public class Socks5Client(Socks5Options options)
{
    public IPEndPoint ProxyEndPoint { get; } = options.ProxyEndPoint;
    private readonly string? _username = options.Username;
    private readonly string? _password = options.Password;

    /// <summary>
    /// Establish a TCP connection to the destination host via the SOCKS5 proxy.
    /// </summary>
    public async Task ConnectAsync(TcpClient tcpClient, IPEndPoint remoteEp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

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

    /// <summary>
    /// Creates a UDP associate with the proxy and returns the UDP relay endpoint (BND.ADDR:BND.PORT) to be used
    /// for sending UDP packets through the proxy. You should send UDP packets to that endpoint using the
    /// BuildUdpRequest helper to encapsulate the target destination & data.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for keeping the control TCP connection (tcpClient) open while using UDP.
    /// RFC 1928 requires the association to be closed when the TCP connection closes.
    /// </remarks>
    public async Task<IPEndPoint> CreateUdpAssociateAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!tcpClient.Connected)
            await tcpClient.ConnectAsync(ProxyEndPoint, cancellationToken);

        var stream = tcpClient.GetStream();
        if (stream.CanWrite && stream.CanRead) {
            // Always perform auth before issuing command.
            await SelectAuth(stream, cancellationToken);
        }

        // Per RFC 1928 we send UDP ASSOCIATE with address 0.0.0.0:0 (or :: for IPv6) to let the server pick.
        var dummyEp = new IPEndPoint(IPAddress.Any, 0);
        var (_, _, bndAddress, bndPort) = await SendCommandAndReadReply(stream, Socks5Command.UdpAssociate, dummyEp, cancellationToken);

        if (bndAddress == null)
            throw new NotSupportedException("Proxy returned an unsupported address type for UDP ASSOCIATE.");

        return new IPEndPoint(bndAddress, bndPort);
    }

    private async Task SelectAuth(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[] {
            5,
            2,
            (byte)Socks5AuthenticationType.NoAuthenticationRequired,
            (byte)Socks5AuthenticationType.UsernamePassword
        };
        await stream.WriteAsync(buffer, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var response = new byte[2];
        await stream.ReadExactlyAsync(response, cancellationToken);
        var authMethod = (Socks5AuthenticationType)response[1];

        switch (authMethod) {
            case Socks5AuthenticationType.UsernamePassword:
                await PerformUserNameAndPasswordAuth(stream, _username ?? string.Empty, _password ?? string.Empty, cancellationToken);
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
        var (_, reply, _, _) = await SendCommandAndReadReply(stream, Socks5Command.Connect, destinationEndPoint, cancellationToken);
        if (reply != Socks5CommandReply.Succeeded)
            throw new SocketException((int)SocketError.NotConnected);
    }

    private static async Task<(Socks5Command command, Socks5CommandReply reply, IPAddress? boundAddress, int boundPort)> 
        SendCommandAndReadReply(
        NetworkStream stream, Socks5Command command, IPEndPoint destinationEndPoint, CancellationToken cancellationToken)
    {
        var addressType = GetDestAddressType(destinationEndPoint.AddressFamily);
        var addressBytes = destinationEndPoint.Address.GetAddressBytes();
        var destPort = GetDestPortBytes(destinationEndPoint.Port);

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
            // Read and discard the rest of the address/port to leave stream clean
            await ReadAndDiscardAddressPort(stream, header[3], cancellationToken);
            HandleProxyCommandError([0, (byte)reply]);
        }

        var (boundAddress, boundPort) = await ReadAddressPort(stream, header[3], cancellationToken);
        return (command, reply, boundAddress, boundPort);
    }

    private static async Task ReadAndDiscardAddressPort(NetworkStream stream, byte atyp, CancellationToken ct)
    {
        var (addrLen, hasDomainLenByte) = atyp switch {
            (byte)Socks5AddressType.IpV4 => (4, false),
            (byte)Socks5AddressType.IpV6 => (16, false),
            (byte)Socks5AddressType.DomainName => (0, true),
            _ => throw new NotSupportedException("Unsupported ATYP in reply.")
        };

        if (hasDomainLenByte) {
            var lenBuf = new byte[1];
            await stream.ReadExactlyAsync(lenBuf, ct);
            addrLen = lenBuf[0];
        }
        var discard = new byte[addrLen + 2]; // address + port
        await stream.ReadExactlyAsync(discard, ct);
    }

    private static async Task<(IPAddress? address, int port)> ReadAddressPort(NetworkStream stream, byte atyp, CancellationToken ct)
    {
        if (atyp == (byte)Socks5AddressType.IpV4) {
            var buf = new byte[4 + 2];
            await stream.ReadExactlyAsync(buf, ct);
            var addr = new IPAddress(buf[..4]);
            var port = (buf[4] << 8) | buf[5];
            return (addr, port);
        }
        if (atyp == (byte)Socks5AddressType.IpV6) {
            var buf = new byte[16 + 2];
            await stream.ReadExactlyAsync(buf, ct);
            var addr = new IPAddress(buf[..16]);
            var port = (buf[16] << 8) | buf[17];
            return (addr, port);
        }
        if (atyp == (byte)Socks5AddressType.DomainName) {
            var lenBuf = new byte[1];
            await stream.ReadExactlyAsync(lenBuf, ct);
            var len = lenBuf[0];
            var buf = new byte[len + 2];
            await stream.ReadExactlyAsync(buf, ct);
            // Domain replies are rare for BND.ADDR; we ignore converting domain -> IP here.
            var port = (buf[len] << 8) | buf[len + 1];
            return (null, port);
        }
        throw new NotSupportedException("Unsupported ATYP in reply.");
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

    // --------------------------- UDP Helpers ---------------------------------

    /// <summary>
    /// Builds a SOCKS5 UDP request datagram into the provided buffer. Returns the total length written.
    /// Buffer must be large enough: 3 (RSV+FRAG) + 1 (ATYP) + addr + 2 (PORT) + data.Length.
    /// </summary>
    public static int WriteUdpRequest(Span<byte> destinationBuffer, IPEndPoint destination, ReadOnlySpan<byte> data)
    {
        var addrBytes = destination.Address.GetAddressBytes();
        var atyp = GetDestAddressType(destination.AddressFamily);
        var needed = 3 + 1 + addrBytes.Length + 2 + data.Length; // RSV(2)+FRAG(1)+ATYP+ADDR+PORT+DATA
        if (destinationBuffer.Length < needed)
            throw new ArgumentException("Destination buffer is too small for the UDP request.", nameof(destinationBuffer));

        destinationBuffer[0] = 0; // RSV
        destinationBuffer[1] = 0; // RSV
        destinationBuffer[2] = 0; // FRAG (no fragmentation)
        destinationBuffer[3] = atyp;
        var offset = 4;
        addrBytes.CopyTo(destinationBuffer[offset..]);
        offset += addrBytes.Length;
        destinationBuffer[offset++] = (byte)(destination.Port >> 8);
        destinationBuffer[offset++] = (byte)(destination.Port & 0xFF);
        data.CopyTo(destinationBuffer[offset..]);
        offset += data.Length;
        return offset; // total length
    }

    /// <summary>
    /// Parses a SOCKS5 UDP response datagram and returns the original source endpoint and a slice containing payload.
    /// </summary>
    public static IPEndPoint ParseUdpResponse(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> payload)
    {
        if (packet.Length < 7) // minimum with IPv4 & no data
            throw new ArgumentException("Packet too short for SOCKS5 UDP response.", nameof(packet));
        if (packet[0] != 0 || packet[1] != 0 || packet[2] != 0)
            throw new NotSupportedException("Fragmented or malformed SOCKS5 UDP packet.");

        var atyp = packet[3];
        var offset = 4;
        IPAddress address;
        switch (atyp) {
            case (byte)Socks5AddressType.IpV4:
                address = new IPAddress(packet.Slice(offset, 4));
                offset += 4;
                break;
            case (byte)Socks5AddressType.IpV6:
                address = new IPAddress(packet.Slice(offset, 16));
                offset += 16;
                break;
            case (byte)Socks5AddressType.DomainName:
                var len = packet[offset];
                offset += 1 + len; // skip domain (not resolving here)
                address = IPAddress.None; // placeholder
                break;
            default:
                throw new NotSupportedException("Unsupported ATYP in UDP response.");
        }
        var port = (packet[offset] << 8) | packet[offset + 1];
        offset += 2;
        payload = packet[offset..];
        return new IPEndPoint(address, port);
    }
}