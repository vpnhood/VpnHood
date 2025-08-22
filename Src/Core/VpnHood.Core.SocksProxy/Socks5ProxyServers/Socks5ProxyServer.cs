using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.SocksProxy.Socks5ProxyClients;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.SocksProxy.Socks5ProxyServers;

public sealed class Socks5ProxyServer(Socks5ProxyServerOptions options)
{
    private readonly TcpListener _listener = new(options.ListenEndPoint);

    public void Start() => _listener.Start(options.Backlog);
    public void Stop() => _listener.Stop();

    public async Task RunAsync(CancellationToken ct)
    {
        Start();
        try {
            while (!ct.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        finally { Stop(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
    {
        using var tcp = client;
        tcp.NoDelay = true;
        var stream = tcp.GetStream();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        cts.CancelAfter(options.HandshakeTimeout);
        var ct = cts.Token;

        try {
            var method = await NegotiateAuthAsync(stream, requireAuth: options.Username != null, ct)
                .ConfigureAwait(false);
            if (method == Socks5AuthenticationType.UsernamePassword) {
                var ok = await HandleUserPassAuthAsync(stream, options.Username!, options.Password ?? string.Empty, ct)
                    .ConfigureAwait(false);
                if (!ok)
                    return;
            }

            // After handshake, switch to server lifetime token
            var (cmd, addressType) = await ReadRequestHeaderAsync(stream, serverCt).ConfigureAwait(false);
            switch (cmd) {
                case Socks5Command.Connect: {
                        var (ip, port) = await ReadDestAsync(stream, addressType, serverCt).ConfigureAwait(false);
                        await HandleConnectAsync(stream, ip, port, serverCt).ConfigureAwait(false);
                        break;
                    }
                case Socks5Command.UdpAssociate: {
                        var (bindAddress, bindPort) = await HandleUdpAssociateAsync(stream, tcp, addressType, serverCt)
                            .ConfigureAwait(false);
                        await ReplyAsync(stream, Socks5CommandReply.Succeeded, bindAddress, bindPort, serverCt)
                            .ConfigureAwait(false);
                        break;
                    }
                default:
                    await ReplyAsync(stream, Socks5CommandReply.CommandNotSupported, IPAddress.Any, 0, serverCt)
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) {
        }
        catch {
        }
    }

    private static async Task<Socks5AuthenticationType> NegotiateAuthAsync(NetworkStream stream, bool requireAuth, CancellationToken ct)
    {
        var hdr = new byte[2];
        await stream.ReadExactlyAsync(hdr, ct).ConfigureAwait(false);
        if (hdr[0] != 5) throw new IOException("Invalid SOCKS version.");
        var nMethods = hdr[1];
        var methods = new byte[nMethods];
        await stream.ReadExactlyAsync(methods, ct).ConfigureAwait(false);
        var supportsUserPass = methods.Contains((byte)Socks5AuthenticationType.UsernamePassword);
        var supportsNoAuth = methods.Contains((byte)Socks5AuthenticationType.NoAuthenticationRequired);

        Socks5AuthenticationType selected;
        if (requireAuth) {
            if (!supportsUserPass) {
                await stream.WriteAsync(new byte[] { 5, (byte)Socks5AuthenticationType.ReplyNoAcceptableMethods }, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                throw new IOException("No acceptable auth method offered.");
            }
            selected = Socks5AuthenticationType.UsernamePassword;
        }
        else {
            selected = supportsNoAuth ? Socks5AuthenticationType.NoAuthenticationRequired : (supportsUserPass ? Socks5AuthenticationType.UsernamePassword : Socks5AuthenticationType.ReplyNoAcceptableMethods);
            if (selected == Socks5AuthenticationType.ReplyNoAcceptableMethods) {
                await stream.WriteAsync(new byte[] { 5, (byte)selected }, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                throw new IOException("No acceptable auth method offered.");
            }
        }

        await stream.WriteAsync(new byte[] { 5, (byte)selected }, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        return selected;
    }

    private static async Task<bool> HandleUserPassAuthAsync(NetworkStream stream, string expectedUser, string expectedPass, CancellationToken ct)
    {
        var v = new byte[2];
        await stream.ReadExactlyAsync(v, ct).ConfigureAwait(false);
        if (v[0] != 1)
            return false;

        var userLengthBuffer = v[1];
        var user = new byte[userLengthBuffer];
        await stream.ReadExactlyAsync(user, ct).ConfigureAwait(false);

        var passwordLengthBuf = new byte[1];
        await stream.ReadExactlyAsync(passwordLengthBuf, ct).ConfigureAwait(false);
        var passwordLength = passwordLengthBuf[0];
        var pass = new byte[passwordLength];
        await stream.ReadExactlyAsync(pass, ct).ConfigureAwait(false);

        var ok = Encoding.UTF8.GetString(user) == expectedUser && Encoding.UTF8.GetString(pass) == expectedPass;
        await stream.WriteAsync(new byte[] { 1, (byte)(ok ? 0 : 0xFF) }, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        return ok;
    }

    private static async Task<(Socks5Command cmd, Socks5AddressType addressType)> ReadRequestHeaderAsync(NetworkStream stream, CancellationToken ct)
    {
        var reqHdr = new byte[4];
        await stream.ReadExactlyAsync(reqHdr, ct).ConfigureAwait(false);
        if (reqHdr[0] != 5) throw new IOException("Invalid SOCKS version.");
        return ((Socks5Command)reqHdr[1], (Socks5AddressType)reqHdr[3]);
    }

    private static async Task HandleConnectAsync(NetworkStream stream, IPAddress destAddress, int destPort, CancellationToken ct)
    {
        try {
            using var remote = new TcpClient(destAddress.AddressFamily);
            remote.NoDelay = true;
            await remote.ConnectAsync(destAddress, destPort, ct).ConfigureAwait(false);
            await ReplyAsync(stream, Socks5CommandReply.Succeeded, ((IPEndPoint)remote.Client.LocalEndPoint!).Address, ((IPEndPoint)remote.Client.LocalEndPoint!).Port, ct).ConfigureAwait(false);
            var remoteStream = remote.GetStream();
            await PumpBidirectionalAsync(stream, remoteStream, ct).ConfigureAwait(false);
        }
        catch {
            await ReplyAsync(stream, Socks5CommandReply.GeneralSocksServerFailure, IPAddress.Any, 0, ct).ConfigureAwait(false);
        }
    }

    private async Task<(IPAddress bindAddress, int bindPort)> HandleUdpAssociateAsync(NetworkStream stream, TcpClient controlTcp, Socks5AddressType addressType, CancellationToken ct)
    {
        _ = await ReadDestAsync(stream, addressType, ct).ConfigureAwait(false);
        var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var localEp = (IPEndPoint)udp.Client.LocalEndPoint!;
        var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = UdpRelayLoopAsync(udp, controlTcp, relayCts.Token);
        _ = MonitorTcpCloseAsync(controlTcp, relayCts, udp);
        return (IPAddress.Any, localEp.Port);
    }

    private static async Task MonitorTcpCloseAsync(TcpClient tcp, CancellationTokenSource cts, UdpClient? udp = null)
    {
        try {
            var b = new byte[1];
            await tcp.GetStream().ReadAsync(b, 0, 1, cts.Token).ConfigureAwait(false);
        }
        catch {
        }
        finally {
            try {
                udp?.Dispose();
            }
            catch {

            }
            await cts.TryCancelAsync();
        }
    }

    private static async Task UdpRelayLoopAsync(UdpClient udp, TcpClient controlTcp, CancellationToken cancellationToken)
    {
        IPEndPoint? clientUdpEp = null;
        while (!cancellationToken.IsCancellationRequested) {
            UdpReceiveResult res;
            try { res = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            var src = res.RemoteEndPoint;
            var bufArray = res.Buffer;
            var length = bufArray.Length;

            if (clientUdpEp == null || src.Equals(clientUdpEp)) {
                if (length < 7 || bufArray[0] != 0 || bufArray[1] != 0 || bufArray[2] != 0) continue;
                var offset = 3;
                var addressType = (Socks5AddressType)bufArray[offset++];
                IPAddress? destinationAddress = null;
                if (addressType == Socks5AddressType.IpV4) {
                    destinationAddress = new IPAddress(new ReadOnlySpan<byte>(bufArray, offset, 4));
                    offset += 4;
                }
                else if (addressType == Socks5AddressType.IpV6) {
                    destinationAddress = new IPAddress(new ReadOnlySpan<byte>(bufArray, offset, 16));
                    offset += 16;
                }
                else if (addressType == Socks5AddressType.DomainName) {
                    var len = bufArray[offset++];
                    var host = Encoding.ASCII.GetString(bufArray, offset, len);
                    offset += len;
                    try {
                        var ips = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                        destinationAddress = ips[0];
                    }
                    catch {
                        continue;
                    }
                }
                else
                    continue;

                var destinationPort = (bufArray[offset] << 8) | bufArray[offset + 1]; offset += 2;
                var payload = new byte[length - offset]; Array.Copy(bufArray, offset, payload, 0, payload.Length);
                clientUdpEp ??= src;
                try {
                    await udp.SendAsync(payload, payload.Length, new IPEndPoint(destinationAddress!, destinationPort))
                        .ConfigureAwait(false);
                }
                catch {

                }
            }
            else {
                var resp = BuildUdpResponse(src, bufArray);
                try {
                    await udp.SendAsync(resp, resp.Length, clientUdpEp).ConfigureAwait(false);
                }
                catch {

                }
            }
        }
    }

    private static async Task PumpBidirectionalAsync(NetworkStream a, NetworkStream b, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = a.CopyToAsync(b, ct);
        var t2 = b.CopyToAsync(a, ct);
        await Task.WhenAny(t1, t2).ConfigureAwait(false);
        await cts.CancelAsync();
    }

    private static byte[] BuildUdpResponse(IPEndPoint src, byte[] payload)
    {
        var addressBytes = src.Address.GetAddressBytes();
        var header = new byte[3 + 1 + addressBytes.Length + 2];
        header[0] = 0; header[1] = 0; header[2] = 0;
        header[3] = (byte)(src.Address.IsV6() ? Socks5AddressType.IpV6 : Socks5AddressType.IpV4);
        var o = 4; Array.Copy(addressBytes, 0, header, o, addressBytes.Length); o += addressBytes.Length;
        header[o++] = (byte)(src.Port >> 8); header[o++] = (byte)(src.Port & 0xFF);
        var resp = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, resp, 0, header.Length);
        Buffer.BlockCopy(payload, 0, resp, header.Length, payload.Length);
        return resp;
    }

    private static async Task<(IPAddress address, int port)> ReadDestAsync(NetworkStream stream, Socks5AddressType addressType, CancellationToken ct)
    {
        switch (addressType) {
            case Socks5AddressType.IpV4: {
                    var buf = new byte[6];
                    await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
                    return (new IPAddress(buf.AsSpan(0, 4)), (buf[4] << 8) | buf[5]);
                }
            case Socks5AddressType.IpV6: {
                    var buf = new byte[18];
                    await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
                    return (new IPAddress(buf.AsSpan(0, 16)), (buf[16] << 8) | buf[17]);
                }
            case Socks5AddressType.DomainName: {
                    var lenBuf = new byte[1];
                    await stream.ReadExactlyAsync(lenBuf, ct).ConfigureAwait(false);
                    var len = lenBuf[0]; var buf = new byte[len + 2];
                    await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
                    var port = (buf[len] << 8) | buf[len + 1];
                    var host = Encoding.ASCII.GetString(buf.AsSpan(0, len));
                    var ips = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                    var ip = Array.Find(ips, a => a.AddressFamily == AddressFamily.InterNetwork) ?? ips[0];
                    return (ip, port);
                }
            default:
                throw new NotSupportedException("Unsupported AddressType.");
        }
    }

    private static async Task ReplyAsync(NetworkStream stream, Socks5CommandReply reply, IPAddress bindAddress, int bindPort, CancellationToken ct)
    {
        var addressBytes = bindAddress.GetAddressBytes();
        var addressType = bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? Socks5AddressType.IpV6 : Socks5AddressType.IpV4;
        var portBytes = new[] { (byte)(bindPort >> 8), (byte)(bindPort & 0xFF) };
        var resp = new byte[4 + addressBytes.Length + 2];
        resp[0] = 5; resp[1] = (byte)reply; resp[2] = 0; resp[3] = (byte)addressType;
        addressBytes.CopyTo(resp, 4); portBytes.CopyTo(resp, 4 + addressBytes.Length);
        await stream.WriteAsync(resp, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
