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
    private bool _disposed;
    private int _tunAdapterFd;
    private IPAddress? _gatewayIpV4;
    private IPAddress? _gatewayIpV6;
    private int _mtu = 0xFFFF;
    private bool _useNat;
    private int? _metric;
    public string AdapterName { get; } = linuxAdapterOptions.AdapterName;
    public IPAddress? PrimaryAdapterIpV4 { get; private set; }
    public IPAddress? PrimaryAdapterIpV6 { get; private set; }
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }

    public bool Started { get; private set; }
    public bool IsNatSupported => true;
    public bool IsDnsServersSupported => true;
    public bool CanProtectSocket => true;
    public bool CanSendPacketToOutbound => false;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;

    public async Task Start(VpnAdapterOptions adapterOptions, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LinuxTunVpnAdapter));

        // get the WAN adapter IP
        PrimaryAdapterIpV4 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53));
        PrimaryAdapterIpV6 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV6()), 53));
        AdapterIpNetworkV4 = adapterOptions.VirtualIpNetworkV4;
        AdapterIpNetworkV6 = adapterOptions.VirtualIpNetworkV6;
        _useNat = linuxAdapterOptions.UseNat;

        // start the adapter
        if (Started)
            Stop();

        try {
            // create tun adapter
            _logger.LogInformation("Initializing {AdapterName} TUN adapter...", AdapterName);
            await Init(cancellationToken).VhConfigureAwait();

            // Open TUN Adapter
            _logger.LogDebug("Open TUN adapter...");
            _tunAdapterFd = OpenTunAdapter(AdapterName, false);

            // Private IP Networks
            _logger.LogDebug("Adding private networks...");
            if (adapterOptions.VirtualIpNetworkV4 != null) {
                _gatewayIpV4 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV4);
                await AddAddress(adapterOptions.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();
            }

            if (adapterOptions.VirtualIpNetworkV6 != null) {
                _gatewayIpV6 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV6);
                await AddAddress(adapterOptions.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // set metric
            _logger.LogDebug("Setting metric...");
            if (adapterOptions.Metric != null) {
                await SetMetric(adapterOptions.Metric.Value,
                    ipV4: adapterOptions.VirtualIpNetworkV4 != null,
                    ipV6: adapterOptions.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set mtu
            if (adapterOptions.Mtu != null) {
                _logger.LogDebug("Setting MTU...");
                _mtu = adapterOptions.Mtu.Value;
                await SetMtu(adapterOptions.Mtu.Value,
                    ipV4: adapterOptions.VirtualIpNetworkV4 != null,
                    ipV6: adapterOptions.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set DNS servers
            _logger.LogDebug("Setting DNS servers...");
            await SetDnsServers(adapterOptions.DnsServers, cancellationToken).VhConfigureAwait();

            // add routes
            _logger.LogDebug("Adding routes...");
            foreach (var network in adapterOptions.IncludeNetworks) {
                var gateway = network.IsV4 ? _gatewayIpV4 : _gatewayIpV6;
                if (gateway != null)
                    await AddRoute(network, gateway, cancellationToken).VhConfigureAwait();
            }

            // add NAT
            if (_useNat) {
                _logger.LogDebug("Adding NAT...");
                if (adapterOptions.VirtualIpNetworkV4 != null)
                    await AddNat(adapterOptions.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();

                if (adapterOptions.VirtualIpNetworkV6 != null)
                    await AddNat(adapterOptions.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // start reading packets
            _ = Task.Run(ReadingPacketTask, CancellationToken.None);

            Started = true;
            _logger.LogInformation("TUN adapter started.");
        }
        catch (ExternalException ex) {
            _logger.LogError(ex, "Failed to start TUN adapter.");
            Stop();
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

    private static string GetMainAdapter()
    {
        var mainInterface = ExecuteCommand("ip route | grep default | awk '{print $5}'");
        mainInterface = mainInterface.Trim();
        if (string.IsNullOrEmpty(mainInterface))
            throw new InvalidOperationException("No active network interface found.");

        return mainInterface;
    }

    private static async Task<string> GetMainAdapterAsync(CancellationToken cancellationToken)
    {
        var mainInterface = await ExecuteCommandAsync("ip route | grep default | awk '{print $5}'", cancellationToken).VhConfigureAwait();
        mainInterface = mainInterface.Trim();
        if (string.IsNullOrEmpty(mainInterface))
            throw new InvalidOperationException("No active network interface found.");

        return mainInterface;
    }

    private static void NatRemove(string mainAdapter, IpNetwork ipNetwork)
    {
        // Remove NAT rule. try until no rule found
        var res = "ok";
        while (!string.IsNullOrEmpty(res)) {
            var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
            res = VhUtils.TryInvoke("Remove NAT rule", () =>
                ExecuteCommand($"{iptables} -t nat -D POSTROUTING -s {ipNetwork} -o {mainAdapter} -j MASQUERADE"));
        }
    }

    private async Task Init(CancellationToken cancellationToken)
    {
        // make sure the adapter is not already started
        ReleaseUnmanagedResources(); // release previous resources if any

        // Create and configure tun interface
        _logger.LogDebug("Creating tun adapter ...");
        await ExecuteCommandAsync($"ip tuntap add dev {AdapterName} mode tun", cancellationToken).VhConfigureAwait();

        // Enable IP forwarding
        _logger.LogDebug("Enabling IP forwarding...");
        await ExecuteCommandAsync("sysctl -w net.ipv4.ip_forward=1", cancellationToken).VhConfigureAwait();
        await ExecuteCommandAsync("sysctl -w net.ipv6.conf.all.forwarding=1", cancellationToken).VhConfigureAwait();

        // Bring up the interface
        _logger.LogDebug("Bringing up the TUN...");
        await ExecuteCommandAsync($"ip link set {AdapterName} up", cancellationToken).VhConfigureAwait();
    }

    private async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // Configure NAT with iptables
        var mainInterface = await GetMainAdapterAsync(cancellationToken).VhConfigureAwait();
        _logger.LogDebug("Setting up NAT with iptables...");
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
        await ExecuteCommandAsync($"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {mainInterface} -j MASQUERADE", cancellationToken).VhConfigureAwait();
    }
    private async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync($"ip addr add {ipNetwork} dev {AdapterName}", cancellationToken).VhConfigureAwait();
    }
    private async Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"ip route add {ipNetwork} dev {AdapterName} via {gatewayIp}"
            : $"ip -6 route add {ipNetwork} dev {AdapterName} via {gatewayIp}";
        
        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
    }

    protected Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _metric = metric;
        return Task.CompletedTask;
    }
    protected async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        var command = $"ip link set dev {AdapterName} mtu {mtu}";
        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
    }

    private async Task SetDnsServers(IPAddress[] ipAddresses, CancellationToken cancellationToken)
    {
        var allDns = string.Join(" ", ipAddresses.Select(x => x.ToString()));
        var command = $"resolvectl dns {AdapterName} {allDns}";
        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
        await ExecuteCommandAsync($"resolvectl domain {AdapterName} \"~.\"", cancellationToken).VhConfigureAwait();
    }
    private static IPAddress? BuildGatewayFromFromNetwork(IpNetwork ipNetwork)
    {
        // Check for small subnets (IPv4: /31, /32 | IPv6: /128)
        return ipNetwork is { IsV4: true, PrefixLength: >= 31 } or { IsV6: true, PrefixLength: 128 }
            ? null
            : IPAddressUtil.Increment(ipNetwork.FirstIpAddress);
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
        ReleaseUnmanagedResources();
        _logger.LogInformation("TUN adapter stopped.");
        Started = false;
    }

    private void ReadingPacketTask()
    {
        var packetList = new List<IPPacket>(linuxAdapterOptions.MaxPacketCount);

        // Read packets from TUN adapter
        try {
            while (Started && !_disposed) {
                ReadFromTun(packetList, _mtu);
                InvokeReadPackets(packetList);
                WaitForTunRead();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in reading packets from TUN adapter.");
        }

        Stop();
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
        while (Started && packetList.Count < packetList.Capacity) {
            var buffer = new byte[mtu];
            var bytesRead = LinuxAPI.read(_tunAdapterFd, buffer, buffer.Length);

            // check for errors
            if (bytesRead <= 0) {
                var errorCode = Marshal.GetLastWin32Error();
                switch (errorCode) {
                    // No data available, wait
                    case LinuxAPI.EAGAIN: return;

                    // Interrupted, retry
                    case LinuxAPI.EINTR:
                        continue;

                    default:
                        throw new PInvokeException("Could not read from TUN.", errorCode);
                }
            }

            // Parse the packet and add to the list
            var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
            packetList.Add(ipPacket);
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

    private static int OpenTunAdapter(string adapterName, bool blockingMode)
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
        SendPacket(ipPacket); //todo
    }

    public void SendPacketToInbound(IList<IPPacket> packets)
    {
        SendPacket(packets); //todo
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket); //todo
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        SendPacket(ipPackets); //todo
    }

    public void SendPacket(IPPacket ipPacket)
    {
        if (!Started)
            throw new InvalidOperationException("TUN adapter is not started.");

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
            await SendPacketAsync(packet).VhConfigureAwait();
    }

    private static string ExecuteCommand(string command)
    {
        return OsUtils.ExecuteCommand("/bin/bash", $"-c \"{command}\"");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }

    private void ReleaseUnmanagedResources()
    {
        if (_tunAdapterFd != 0) {
            LinuxAPI.close(_tunAdapterFd);
            _tunAdapterFd = 0;
        }

        // Remove existing tun interface
        _logger.LogDebug("Removing existing {AdapterName} TUN adapter (if any)...", AdapterName);
        VhUtils.TryInvoke($"remove existing {AdapterName} TUN adapter", () =>
            ExecuteCommand($"ip link delete {AdapterName}"));


        // Remove previous NAT iptables record
        if (_useNat) {
            var mainAdapter = GetMainAdapter();
            _logger.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...", AdapterName);
            if (AdapterIpNetworkV4 != null)
                NatRemove(mainAdapter, AdapterIpNetworkV4);

            if (AdapterIpNetworkV6 != null)
                NatRemove(mainAdapter, AdapterIpNetworkV6);
        }

    }

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;

        // release managed resources when disposing
        if (disposing) {
            // if started, close the adapter
            if (Started)
                Stop();

            // notify the subscribers that the adapter is disposed
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        ReleaseUnmanagedResources();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}
