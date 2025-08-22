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

    public void Start() => _listener.Start();
    public void Stop() => _listener.Stop();

    public async Task RunAsync(CancellationToken ct)
    {
        Start();
        try {
            while (!ct.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
        }
        finally {
            Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var tcp = client;
        var stream = tcp.GetStream();

        // Greeting
        var hdr = new byte[2];
        await stream.ReadExactlyAsync(hdr, ct).ConfigureAwait(false); // VER, N-METHODS
        if (hdr[0] != 5) { tcp.Close(); return; }
        var nMethods = hdr[1];
        var methods = new byte[nMethods];
        await stream.ReadExactlyAsync(methods, ct).ConfigureAwait(false);

        var requireAuth = options.Username != null;
        var selected = requireAuth ? (byte)Socks5AuthenticationType.UsernamePassword : (byte)Socks5AuthenticationType.NoAuthenticationRequired;
        await stream.WriteAsync(new byte[] { 5, selected }, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Optional RFC1929
        if (requireAuth) {
            var v = new byte[2];
            await stream.ReadExactlyAsync(v, ct).ConfigureAwait(false);
            if (v[0] != 1) { tcp.Close(); return; }
            var ulen = v[1];
            var user = new byte[ulen];
            await stream.ReadExactlyAsync(user, ct).ConfigureAwait(false);
            var plenBuf = new byte[1];
            await stream.ReadExactlyAsync(plenBuf, ct).ConfigureAwait(false);
            var plen = plenBuf[0];
            var pass = new byte[plen];
            await stream.ReadExactlyAsync(pass, ct).ConfigureAwait(false);
            var ok = Encoding.UTF8.GetString(user) == options.Username && Encoding.UTF8.GetString(pass) == options.Password;
            await stream.WriteAsync(new byte[] { 1, (byte)(ok ? 0 : 0xFF) }, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            if (!ok) { tcp.Close(); return; }
        }

        // Request: VER, CMD, RSV, AddressType
        var reqHdr = new byte[4];
        await stream.ReadExactlyAsync(reqHdr, ct).ConfigureAwait(false);
        if (reqHdr[0] != 5) { tcp.Close(); return; }
        var cmd = (Socks5Command)reqHdr[1];
        var socks5AddressType = (Socks5AddressType)reqHdr[3];

        if (cmd == Socks5Command.Connect) {
            var (destAddress, destPort) = await ReadDestAsync(stream, socks5AddressType, ct).ConfigureAwait(false);
            await HandleConnectAsync(stream, destAddress, destPort, ct).ConfigureAwait(false);
            return;
        }
        if (cmd == Socks5Command.UdpAssociate) {
            var (bindAddr, bindPort) = await HandleUdpAssociateAsync(stream, tcp, socks5AddressType, ct).ConfigureAwait(false);
            // Reply success with the UDP bind endpoint (address may be 0.0.0.0 per RFC)
            await ReplyAsync(stream, Socks5CommandReply.Succeeded, bindAddr, bindPort, ct).ConfigureAwait(false);
            // keep TCP open while UDP relay runs inside HandleUdpAssociateAsync
            return;
        }

        // Unsupported command
        await ReplyAsync(stream, Socks5CommandReply.CommandNotSupported, IPAddress.Any, 0, ct).ConfigureAwait(false);
    }

    private static async Task HandleConnectAsync(NetworkStream stream, IPAddress destAddr, int destPort, CancellationToken ct)
    {
        try {
            var remote = new TcpClient(destAddr.AddressFamily);
            await remote.ConnectAsync(destAddr, destPort, ct).ConfigureAwait(false);
            await ReplyAsync(stream, Socks5CommandReply.Succeeded, ((IPEndPoint)remote.Client.LocalEndPoint!).Address, ((IPEndPoint)remote.Client.LocalEndPoint!).Port, ct).ConfigureAwait(false);
            using var remoteClient = remote;
            var remoteStream = remoteClient.GetStream();
            var t1 = stream.CopyToAsync(remoteStream, ct);
            var t2 = remoteStream.CopyToAsync(stream, ct);
            await Task.WhenAny(t1, t2).ConfigureAwait(false);
        }
        catch {
            await ReplyAsync(stream, Socks5CommandReply.GeneralSocksServerFailure, IPAddress.Any, 0, ct).ConfigureAwait(false);
        }
    }

    private async Task<(IPAddress bindAddr, int bindPort)> HandleUdpAssociateAsync(NetworkStream stream, TcpClient controlTcp, 
        Socks5AddressType addressType, CancellationToken cancellationToken)
    {
        // Read and ignore the destination address/port from the request per RFC (client's UDP endpoint hint)
        _ = await ReadDestAsync(stream, addressType, cancellationToken).ConfigureAwait(false);

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var localEp = (IPEndPoint)udp.Client.LocalEndPoint!;

        // Start UDP relay loop
        var relayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = UdpRelayLoopAsync(udp, controlTcp, relayCts.Token);

        // Cancel relay when TCP closes
        _ = MonitorTcpCloseAsync(controlTcp, relayCts);
        return (IPAddress.Any, localEp.Port);
    }

    private static async Task MonitorTcpCloseAsync(TcpClient tcp, CancellationTokenSource cancellationTokenSource)
    {
        try {
            var buf = new byte[1];
            var ns = tcp.GetStream();
            _ = await ns.ReadAsync(buf, 0, 1, cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch { /* ignore */ }
        finally {
            await cancellationTokenSource.TryCancelAsync();
        }
    }

    private static async Task UdpRelayLoopAsync(UdpClient udp, TcpClient controlTcp, CancellationToken ct)
    {
        IPEndPoint? clientUdpEp = null;
        while (!ct.IsCancellationRequested) {
            UdpReceiveResult res;
            try { res = await udp.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            var src = res.RemoteEndPoint;
            var bufArray = res.Buffer; // keep as array to avoid span lifetime issues
            var length = bufArray.Length;

            if (clientUdpEp == null || src.Equals(clientUdpEp)) {
                if (length < 7) continue;
                if (bufArray[0] != 0 || bufArray[1] != 0 || bufArray[2] != 0) continue;
                var offset = 3;
                var addressType = (Socks5AddressType)bufArray[offset++];
                IPAddress? destinationAddress;
                if (addressType == Socks5AddressType.IpV4) { destinationAddress = new IPAddress(new ReadOnlySpan<byte>(bufArray, offset, 4)); offset += 4; }
                else if (addressType == Socks5AddressType.IpV6) { destinationAddress = new IPAddress(new ReadOnlySpan<byte>(bufArray, offset, 16)); offset += 16; }
                else if (addressType == Socks5AddressType.DomainName) {
                    var len = bufArray[offset++];
                    var host = Encoding.ASCII.GetString(bufArray, offset, len);
                    offset += len;
                    try { var ips = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false); destinationAddress = ips[0]; }
                    catch { continue; }
                }
                else continue;

                var dstPort = bufArray[offset] << 8 | bufArray[offset + 1];
                offset += 2;
                var payload = new byte[length - offset];
                Array.Copy(bufArray, offset, payload, 0, payload.Length);

                clientUdpEp ??= src;
                try { await udp.SendAsync(payload, payload.Length, new IPEndPoint(destinationAddress!, dstPort)).ConfigureAwait(false); }
                catch { }
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

    private static byte[] BuildUdpResponse(IPEndPoint src, byte[] payload)
    {
        var address = src.Address;
        var addressBytes = address.GetAddressBytes();
        var headerLen = 3 + 1 + addressBytes.Length + 2; // RSV/FRAG + ADDRESS_TYPE + ADDRESS + PORT
        var buf = new byte[headerLen + payload.Length];
        buf[0] = 0; buf[1] = 0; buf[2] = 0;
        buf[3] = (byte)(address.IsV6() ? Socks5AddressType.IpV6 : Socks5AddressType.IpV4);
        var o = 4;
        Array.Copy(addressBytes, 0, buf, o, addressBytes.Length); o += addressBytes.Length;
        buf[o++] = (byte)(src.Port >> 8);
        buf[o++] = (byte)(src.Port & 0xFF);
        Array.Copy(payload, 0, buf, o, payload.Length);
        return buf;
    }

    private static async Task<(IPAddress addr, int port)> ReadDestAsync(NetworkStream stream, Socks5AddressType addressType, 
        CancellationToken ct)
    {
        if (addressType == Socks5AddressType.IpV4) {
            var buf = new byte[6];
            await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
            return (new IPAddress(buf.AsSpan(0, 4)), buf[4] << 8 | buf[5]);
        }

        if (addressType == Socks5AddressType.IpV6) {
            var buf = new byte[18];
            await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
            return (new IPAddress(buf.AsSpan(0, 16)), buf[16] << 8 | buf[17]);
        }

        if (addressType == Socks5AddressType.DomainName) {
            var lenBuf = new byte[1];
            await stream.ReadExactlyAsync(lenBuf, ct).ConfigureAwait(false);
            var len = lenBuf[0];
            var buf = new byte[len + 2];
            await stream.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
            var port = buf[len] << 8 | buf[len + 1];
            var host = Encoding.ASCII.GetString(buf.AsSpan(0, len));
            var ips = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var ip = Array.Find(ips, a => a.AddressFamily == AddressFamily.InterNetwork) ?? ips[0];
            return (ip, port);
        }

        throw new NotSupportedException("Unsupported AddressType.");
    }

    private static async Task ReplyAsync(NetworkStream stream, Socks5CommandReply reply, IPAddress bindAddress, int bndPort, CancellationToken ct)
    {
        var addressBytes = bindAddress.GetAddressBytes();
        var addressType = bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? Socks5AddressType.IpV6 : Socks5AddressType.IpV4;
        var portBytes = new[] { (byte)(bndPort >> 8), (byte)(bndPort & 0xFF) };
        var resp = new byte[4 + addressBytes.Length + 2];
        resp[0] = 5; resp[1] = (byte)reply; resp[2] = 0; resp[3] = (byte)addressType;
        addressBytes.CopyTo(resp, 4);
        portBytes.CopyTo(resp, 4 + addressBytes.Length);
        await stream.WriteAsync(resp, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
