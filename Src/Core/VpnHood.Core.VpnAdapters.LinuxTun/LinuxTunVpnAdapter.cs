using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

namespace VpnHood.Core.VpnAdapters.LinuxTun;
public class LinuxTunVpnAdapter(LinuxTunVpnAdapterOptions linuxAdapterOptions) : IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs = (int)linuxAdapterOptions.MaxPacketSendDelay.TotalMilliseconds;
    private readonly ILogger _logger = linuxAdapterOptions.Logger;
    private int _disposed;
    private int _tunAdapterFd;
    private IPAddress? _gatewayIpV4;
    private IPAddress? _gatewayIpV6;
    private int _mtu = 0xFFFF;
    public string AdapterName { get; } = linuxAdapterOptions.AdapterName;
    public IPAddress? PrimaryAdapterIpV4 { get; private set; }
    public IPAddress? PrimaryAdapterIpV6 { get; private set; }
    public bool Started { get; private set; }
    public bool IsDnsServersSupported => true;
    public bool CanProtectSocket => true;
    public bool CanSendPacketToOutbound => false;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;

    public async Task StartCapture(VpnAdapterOptions adapterOptions, CancellationToken cancellationToken)
    {
        if (_disposed == 1)
            throw new ObjectDisposedException(nameof(LinuxTunVpnAdapter));

        if (Started)
            await StopCapture(cancellationToken);

        try {
            // get the WAN adapter IP
            PrimaryAdapterIpV4 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53));
            PrimaryAdapterIpV6 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV6()), 53));

            // create WinTun adapter
            _logger.LogInformation("Initializing WinTun Adapter...");
            _tunAdapterFd = OpenTunDevice(AdapterName, false);

            // create an event object to wait for packets
            _logger.LogDebug("Creating event object for WinTun...");

            // Private IP Networks
            _logger.LogDebug("Adding private IP networks...");
            if (adapterOptions.VirtualIpNetworkV4 != null) {
                _gatewayIpV4 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV4);
                await AddAddress(adapterOptions.VirtualIpNetworkV4, cancellationToken);
            }

            if (adapterOptions.VirtualIpNetworkV6 != null) {
                _gatewayIpV6 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV6);
                await AddAddress(adapterOptions.VirtualIpNetworkV6, cancellationToken);
            }

            // set metric
            _logger.LogDebug("Setting metric...");
            await SetMetric(adapterOptions.Metric, IPVersion.IPv4, cancellationToken);
            await SetMetric(adapterOptions.Metric, IPVersion.IPv6, cancellationToken);

            // set mtu
            if (adapterOptions.Mtu != null) {
                _logger.LogDebug("Setting MTU...");
                _mtu = adapterOptions.Mtu.Value;
                if (adapterOptions.VirtualIpNetworkV4 != null)
                    await SetMtu(adapterOptions.Mtu.Value, IPVersion.IPv4, cancellationToken);

                if (adapterOptions.VirtualIpNetworkV6 != null)
                    await SetMtu(adapterOptions.Mtu.Value, IPVersion.IPv6, cancellationToken);
            }

            // set DNS servers
            _logger.LogDebug("Setting DNS servers...");
            foreach (var dnsServer in adapterOptions.DnsServers)
                await AddDns(dnsServer, cancellationToken);

            // add routes
            _logger.LogDebug("Adding routes...");
            foreach (var network in adapterOptions.IncludeNetworks) {
                var gateway = network.IsV4 ? _gatewayIpV4 : _gatewayIpV6;
                if (gateway != null)
                    await AddRoute(network, gateway, cancellationToken);
            }

            // start reading packets
            _ = Task.Run(ReadingPacketTask, CancellationToken.None);

            Started = true;
            _logger.LogInformation("WinTun adapter started.");
        }
        catch {
            await StopCapture(cancellationToken);
            throw;
        }
    }

    private IPAddress? GetWanAdapterIp(IPEndPoint remoteEndPoint)
    {
        try {
            using var udpClient = new UdpClient();
            udpClient.Connect(remoteEndPoint);
            return (udpClient.Client.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception) {
            _logger.LogDebug("Failed to get WAN adapter IP. RemoteEndPoint: {RemoteEndPoint}",
                remoteEndPoint);
            return null;
        }
    }

    private async Task SetMetric(int metric, IPVersion ipVersion, CancellationToken cancellationToken)
    {
        var ipVersionStr = ipVersion == IPVersion.IPv4 ? "ipv4" : "ipv6";
        var command = $"interface {ipVersionStr} set interface \"{AdapterName}\" metric={metric}";
        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    private async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 set address \"{AdapterName}\" static {ipNetwork}"
            : $"interface ipv6 set address \"{AdapterName}\" {ipNetwork}";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }


    private async Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 add route {ipNetwork} \"{AdapterName}\" {gatewayIp}"
            : $"interface ipv6 add route {ipNetwork} \"{AdapterName}\" {gatewayIp}";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    private async Task SetMtu(int mtu, IPVersion ipVersion, CancellationToken cancellationToken)
    {
        var ipVersionStr = ipVersion == IPVersion.IPv4 ? "ipv4" : "ipv6";
        var command = $"interface {ipVersionStr} set subinterface \"{AdapterName}\" mtu={mtu}";
        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    private async Task AddDns(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var command = $"interface ip add dns \"{AdapterName}\" {ipAddress}";
        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }
    private static IPAddress? BuildGatewayFromFromNetwork(IpNetwork ipNetwork)
    {
        // Check for small subnets (IPv4: /31, /32 | IPv6: /128)
        return ipNetwork is { IsV4: true, PrefixLength: >= 31 } or { IsV6: true, PrefixLength: 128 }
            ? null
            : IPAddressUtil.Increment(ipNetwork.FirstIpAddress);
    }

    public Task StopCapture(CancellationToken cancellationToken)
    {
        if (!Started)
            return Task.CompletedTask;

        _logger.LogInformation("Stopping WinTun adapter...");
        Started = false;
        ReleaseSessionUnmanagedResources();
        _logger.LogInformation("WinTun adapter stopped.");
        return Task.CompletedTask;
    }

    private Task ReadingPacketTask()
    {
        var packetList = new List<IPPacket>(linuxAdapterOptions.MaxPacketCount);

        // Read packets from Tun adapter
        try {
            while (Started && _disposed == 0) {
                ReadFromTun(packetList, _mtu);
                InvokeReadPackets(packetList);
                WaitForTunRead();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in reading packets from WinTun adapter.");
        }

        _ = StopCapture(CancellationToken.None);
        return Task.CompletedTask;
    }
    private void WaitForTunRead()
    {
        WaitForTun(PollEvent.In);
    }
    private void WaitForTunWrite()
    {
        WaitForTun(PollEvent.Out);
    }
    private void WaitForTun(PollEvent pollEvent)
    {
        var pollFd = new PollFD {
            fd = _tunAdapterFd,
            events = (short)pollEvent
        };

        while (true) {
            var result = LinuxAPI.poll([pollFd], 1, -1); // Blocks until data arrives
            if (result >= 0)
                break; // Success, exit loop

            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == LinuxAPI.EINTR)
                continue; // Poll was interrupted, retry

            throw new PInvokeException("Failed to poll the TUN device for new data.", errorCode);
        }
    }
    private void InvokeReadPackets(List<IPPacket> packetList)
    {
        try {
            if (packetList.Count > 0)
                PacketReceivedFromInbound?.Invoke(this, new PacketReceivedEventArgs(packetList));
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in invoking packet received event.");
        }
        finally {
            packetList.Clear();
        }
    }

    private void WriteToTun(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length) {
            var bytesWritten = LinuxAPI.write(_tunAdapterFd, buffer, buffer.Length - offset);
            if (bytesWritten > 0) {
                offset += bytesWritten; // Advance buffer
                continue;
            }

            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode) {
                // Buffer full, wait
                case LinuxAPI.EAGAIN:
                    WaitForTunWrite();
                    continue;

                // Interrupted, retry
                case LinuxAPI.EINTR:
                    continue;

                default:
                    throw new PInvokeException("Could not write to TUN.", errorCode);
            }
        }
    }

    private void ReadFromTun(List<IPPacket> packetList, int mtu)
    {
        while (Started) {
            // Non-blocking read loop
            var buffer = new byte[mtu];
            var bytesRead = LinuxAPI.read(_tunAdapterFd, buffer, buffer.Length);
            if (bytesRead > 0) {

                var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
                packetList.Add(ipPacket);
                if (packetList.Count >= packetList.Capacity)
                    break;
            }

            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode) {
                // No data available, wait
                case LinuxAPI.EAGAIN:
                    return;

                // Interrupted, retry
                case LinuxAPI.EINTR:
                    continue;

                default:
                    throw new PInvokeException("Could not read to TUN.", errorCode);
            }
        }
    }
    public void ProtectSocket(Socket socket)
    {
        throw new NotImplementedException();
    }

    public UdpClient CreateProtectedUdpClient(int port, AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new UdpClient(new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new UdpClient(new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => new UdpClient(port, addressFamily)
        };
    }

    public TcpClient CreateProtectedTcpClient(int port, AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new TcpClient(new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new TcpClient(new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => throw new InvalidOperationException(
                "Could not create a protected TCP client because the primary adapter IP is not available.")
        };
    }

    private static int OpenTunDevice(string adapterName, bool blockingMode)
    {
        // Open the TUN device file
        var tunDeviceFd = LinuxAPI.open("/dev/net/tun", LinuxAPI.ORdwr);
        if (tunDeviceFd < 0)
            throw new InvalidOperationException("Failed to open TUN device.");

        // Configure the device
        var ifr = new Ifreq {
            ifr_name = adapterName,
            ifr_flags = LinuxAPI.IFF_TUN | LinuxAPI.IFF_NO_PI
        };

        var ioctlResult = LinuxAPI.ioctl(tunDeviceFd, LinuxAPI.TUNSETIFF, ref ifr);
        if (ioctlResult < 0) {
            LinuxAPI.close(tunDeviceFd);
            throw new PInvokeException($"Failed to configure TUN device. IoctlResult: {ioctlResult}");
        }

        if (!blockingMode) {
            if (LinuxAPI.fcntl(tunDeviceFd, LinuxAPI.F_SETFL, LinuxAPI.O_NONBLOCK) < 0) {
                LinuxAPI.close(tunDeviceFd);
                throw new PInvokeException("Failed to set TUN device to non-blocking mode.");
            }
        }

        return tunDeviceFd;
    }
    public void SendPacketToInbound(IPPacket ipPacket)
    {
        throw new NotImplementedException();
    }

    public void SendPacketToInbound(IList<IPPacket> packets)
    {
        throw new NotImplementedException();
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
        throw new NotImplementedException();
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        throw new NotImplementedException();
    }

    public void SendPacket(IPPacket ipPacket)
    {
        if (!Started)
            throw new InvalidOperationException("WinTun adapter is not started.");

        WaitForTunWrite();
        WriteToTun(ipPacket.Bytes);
    }

    public void SendPacket(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    private readonly SemaphoreSlim _sendPacketSemaphoreSlim = new(1, 1);

    public Task SendPacketAsync(IPPacket ipPacket)
    {
        return _sendPacketSemaphoreSlim.WaitAsync().ContinueWith(_ => {
            try {
                SendPacket(ipPacket);
            }
            finally {
                _sendPacketSemaphoreSlim.Release();
            }
        });
    }

    public async Task SendPacketAsync(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            await SendPacketAsync(packet);
    }

    private void ReleaseSessionUnmanagedResources()
    {
        LinuxAPI.close(_tunAdapterFd);
        _tunAdapterFd = 0;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // release managed resources when disposing
        if (disposing) {
            // if started, close the adapter
            if (Started)
                _ = StopCapture(CancellationToken.None);

            // notify the subscribers that the adapter is disposed
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        ReleaseSessionUnmanagedResources();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}