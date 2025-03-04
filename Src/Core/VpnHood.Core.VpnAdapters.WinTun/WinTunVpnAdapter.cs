using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using PacketDotNet;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.WinTun.WinNative;

namespace VpnHood.Core.VpnAdapters.WinTun;

public class WinTunVpnAdapter(WinTunVpnAdapterOptions adapterOptions) : IVpnAdapter
{
    private readonly List<IPPacket> _packetList = new(adapterOptions.MaxPacketCount);
    private readonly int _ringCapacity = adapterOptions.RingCapacity;
    private readonly int _maxPacketSendDelayMs = (int)adapterOptions.MaxPacketSendDelay.TotalMilliseconds;
    private readonly ILogger _logger = adapterOptions.Logger;
    private bool _disposed;
    private IntPtr _readEvent;
    private IntPtr _tunAdapter;
    private IntPtr _tunSession;
    private IPAddress? _gatewayIpV4;
    private IPAddress? _gatewayIpV6;

    public const int MinRingCapacity = 0x20000; // 128kiB
    public const int MaxRingCapacity = 0x4000000; // 64MiB
    public string AdapterName { get; } = adapterOptions.AdapterName;
    public IPAddress? PrimaryAdapterIpV4 { get; private set; }
    public IPAddress? PrimaryAdapterIpV6 { get; private set; }
    public bool Started { get; private set; }
    public bool IsNatSupported => false;
    public bool IsDnsServersSupported => true;
    public bool CanProtectSocket => false;
    public bool CanSendPacketToOutbound => false;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;

    public async Task Start(VpnAdapterOptions adapterOptions, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WinTunVpnAdapter));

        if (Started)
            Stop();

        try
        {
            // get the WAN adapter IP
            PrimaryAdapterIpV4 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53));
            PrimaryAdapterIpV6 = GetWanAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV6()), 53));

            // create WinTun adapter
            _logger.LogInformation("Initializing WinTun Adapter...");
            _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "WinTun", IntPtr.Zero);
            if (_tunAdapter == IntPtr.Zero)
                throw new Win32Exception("Failed to create WinTun adapter.");

            // start WinTun session
            _logger.LogInformation("Starting WinTun session...");
            _tunSession = WinTunApi.WintunStartSession(_tunAdapter, _ringCapacity);
            if (_tunSession == IntPtr.Zero)
                throw new Win32Exception("Failed to start WinTun session.");

            // create an event object to wait for packets
            _logger.LogDebug("Creating event object for WinTun...");
            _readEvent = WinTunApi.WintunGetReadWaitEvent(_tunSession); // do not close this handle by documentation

            // Private IP Networks
            _logger.LogDebug("Adding private IP networks...");
            if (adapterOptions.VirtualIpNetworkV4 != null)
            {
                _gatewayIpV4 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV4);
                await AddAddress(adapterOptions.VirtualIpNetworkV4, cancellationToken);
            }

            if (adapterOptions.VirtualIpNetworkV6 != null)
            {
                _gatewayIpV6 = BuildGatewayFromFromNetwork(adapterOptions.VirtualIpNetworkV6);
                await AddAddress(adapterOptions.VirtualIpNetworkV6, cancellationToken);
            }

            // set metric
            _logger.LogDebug("Setting metric...");
            if (adapterOptions.Metric != null) {
                await SetMetric(adapterOptions.Metric.Value, IPVersion.IPv4, cancellationToken);
                await SetMetric(adapterOptions.Metric.Value, IPVersion.IPv6, cancellationToken);
            }

            // set mtu
            if (adapterOptions.Mtu != null)
            {
                _logger.LogDebug("Setting MTU...");
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
            foreach (var network in adapterOptions.IncludeNetworks)
            {
                var gateway = network.IsV4 ? _gatewayIpV4 : _gatewayIpV6;
                if (gateway != null)
                    await AddRoute(network, gateway, cancellationToken);
            }

            // start reading packets
            _ = Task.Run(ReadingPacketTask, CancellationToken.None);

            Started = true;
            _logger.LogInformation("WinTun adapter started.");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private IPAddress? GetWanAdapterIp(IPEndPoint remoteEndPoint)
    {
        try
        {
            using var udpClient = new UdpClient();
            udpClient.Connect(remoteEndPoint);
            return (udpClient.Client.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception)
        {
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

    public void Stop()
    {
        if (!Started)
            return;

        _logger.LogInformation("Stopping WinTun adapter...");
        Started = false;
        ReleaseUnmanagedResources();
        _logger.LogInformation("WinTun adapter stopped.");
    }

    private Task ReadingPacketTask()
    {
        try
        {
            using var waitHandle = new AutoResetEvent(false);
            waitHandle.SafeWaitHandle = new SafeWaitHandle(_readEvent, false);

            while (Started && !_disposed)
            {
                // Wait until a packet is available
                waitHandle.WaitOne();
                ReadingWinTunPackets();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reading packets from WinTun adapter.");
        }

        Stop();
        return Task.CompletedTask;
    }

    private void ReadingWinTunPackets()
    {
        const int maxErrorCount = 10;
        var errorCount = 0;
        _packetList.Clear();
        while (Started)
        {
            var tunReceivePacket = WinTunApi.WintunReceivePacket(_tunSession, out var size);
            var lastError = (WintunReceivePacketError)Marshal.GetLastWin32Error();
            if (tunReceivePacket != IntPtr.Zero)
            {
                errorCount = 0; // reset the error count
                try
                {
                    // read the packet
                    var buffer = new byte[size];
                    Marshal.Copy(tunReceivePacket, buffer, 0, size);
                    var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
                    _packetList.Add(ipPacket);

                    // if the packet list is full, send it
                    if (_packetList.Count == _packetList.Capacity)
                        InvokeReadPackets();

                }
                finally
                {
                    WinTunApi.WintunReleaseReceivePacket(_tunSession, tunReceivePacket);
                }
                continue;
            }

            // flush remaining packets on any error
            InvokeReadPackets();

            switch (lastError)
            {
                case WintunReceivePacketError.NoMoreItems:
                    return;

                case WintunReceivePacketError.HandleEof:
                    if (Started)
                        throw new InvalidOperationException("WinTun adapter has been closed.");
                    return;

                case WintunReceivePacketError.InvalidData:
                    _logger.LogWarning("Invalid data received from WinTun adapter.");
                    if (errorCount++ > maxErrorCount)
                        throw new InvalidOperationException("Too many invalid data received from WinTun adapter."); // read the next packet
                    continue; // read the next packet

                default:
                    _logger.LogDebug("Unknown error in reading packet from WinTun. LastError: {lastError}", lastError);
                    if (errorCount++ > maxErrorCount)
                        throw new InvalidOperationException("Too many errors in reading packet from WinTun."); // read the next packet
                    continue; // read the next packet
            }
        }
    }

    private void InvokeReadPackets()
    {
        try
        {
            if (_packetList.Count > 0)
                PacketReceivedFromInbound?.Invoke(this, new PacketReceivedEventArgs(_packetList));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in invoking packet received event.");
        }
        finally
        {
            _packetList.Clear();
        }
    }

    public void ProtectSocket(Socket socket)
    {
        throw new NotImplementedException();
    }

    public UdpClient CreateProtectedUdpClient(int port, AddressFamily addressFamily)
    {
        return addressFamily switch
        {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new UdpClient(new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new UdpClient(new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => new UdpClient(port, addressFamily)
        };
    }

    public TcpClient CreateProtectedTcpClient(int port, AddressFamily addressFamily)
    {
        return addressFamily switch
        {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new TcpClient(new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new TcpClient(new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => throw new InvalidOperationException(
                "Could not create a protected TCP client because the primary adapter IP is not available.")
        };
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
            throw new InvalidOperationException("WinTun adapter is not started.");

        // Allocate memory for the packet inside WinTun ring buffer
        var sent = false;
        var delay = 5;
        while (true)
        {
            var packetBytes = ipPacket.Bytes;
            var packetMemory = WinTunApi.WintunAllocateSendPacket(_tunSession, packetBytes.Length); // thread-safe
            if (packetMemory != IntPtr.Zero)
            {
                // Copy the raw packet data into WinTun memory
                Marshal.Copy(packetBytes, 0, packetMemory, packetBytes.Length);

                // Send the packet through WinTun
                WinTunApi.WintunSendPacket(_tunSession, packetMemory); // thread-safe
                sent = true;
                break;
            }

            // if failed to send, drop the packet
            if (delay > _maxPacketSendDelayMs)
                break;

            // wait and try again
            Thread.Sleep(delay);
            delay *= 2;
        }

        // log if failed to send
        if (!sent)
            _logger.LogWarning("Failed to send packet via WinTun adapter.");
    }

    public void SendPacket(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    private readonly SemaphoreSlim _sendPacketSemaphoreSlim = new(1, 1);
    public Task SendPacketAsync(IPPacket ipPacket)
    {
        return _sendPacketSemaphoreSlim.WaitAsync().ContinueWith(_ =>
        {
            try
            {
                SendPacket(ipPacket);
            }
            finally
            {
                _sendPacketSemaphoreSlim.Release();
            }
        });
    }

    public async Task SendPacketAsync(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            await SendPacketAsync(packet);
    }

    protected void ReleaseUnmanagedResources()
    {
        if (_tunSession != IntPtr.Zero)
        {
            WinTunApi.WintunEndSession(_tunSession);
            _tunSession = IntPtr.Zero;
        }

        // do not close this handle by documentation
        _readEvent = IntPtr.Zero;

        // close the adapter
        if (_tunAdapter != IntPtr.Zero)
        {
            WinTunApi.WintunCloseAdapter(_tunAdapter);
            _tunAdapter = IntPtr.Zero;
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~WinTunVpnAdapter()
    {
        Dispose(false);
    }
}