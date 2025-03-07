using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public abstract class TunVpnAdapter(TunVpnAdapterSettings adapterSettings) : IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs = (int)adapterSettings.MaxPacketSendDelay.TotalMilliseconds;
    private int _mtu = 0xFFFF;
    private readonly int _maxAutoRestartCount = adapterSettings.MaxAutoRestartCount;
    private int _autoRestartCount;
    private bool _started;

    protected bool IsDisposed { get; private set; }
    protected ILogger Logger { get; } = adapterSettings.Logger;
    protected bool UseNat { get; private set; }
    public abstract bool IsAppFilterSupported { get; }
    public abstract bool IsDnsServerSupported { get; }
    public abstract bool IsNatSupported { get; }
    protected abstract bool CanProtectSocket { get; }
    protected abstract string? AppPackageId { get; }
    protected abstract Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken);
    protected abstract Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken);
    protected abstract Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task SetSessionName(string sessionName, CancellationToken cancellationToken);
    protected abstract Task SetAllowedApps(string[] packageIds, CancellationToken cancellationToken);
    protected abstract Task SetDisallowedApps(string[] packageIds, CancellationToken cancellationToken);
    protected abstract Task AdapterAdd(CancellationToken cancellationToken);
    protected abstract void AdapterRemove();
    protected abstract Task AdapterOpen(CancellationToken cancellationToken);
    protected abstract void AdapterClose();
    protected abstract void WaitForTunWrite();
    protected abstract void WaitForTunRead();
    protected abstract IPPacket? ReadPacket(int mtu);
    protected abstract bool WritePacket(IPPacket ipPacket);
    protected abstract void ProtectSocket(Socket socket);

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler? Disposed;
    public virtual bool CanProtectClient => true;
    public string AdapterName { get; } = adapterSettings.AdapterName;
    public IPAddress? PrimaryAdapterIpV4 { get; private set; }
    public IPAddress? PrimaryAdapterIpV6 { get; private set; }
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }
    public IPAddress? GatewayIpV4 { get; private set; }
    public IPAddress? GatewayIpV6 { get; private set; }
    public bool Started => _started && !IsDisposed;

    public async Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (UseNat && !IsNatSupported)
            throw new NotSupportedException("NAT is not supported by this adapter.");

        Logger.LogInformation("Starting {AdapterName} adapter.", AdapterName);

        // get the WAN adapter IP
        PrimaryAdapterIpV4 =
            GetPrimaryAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53));
        PrimaryAdapterIpV6 =
            GetPrimaryAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV6()), 53));
        AdapterIpNetworkV4 = options.VirtualIpNetworkV4;
        AdapterIpNetworkV6 = options.VirtualIpNetworkV6;
        UseNat = options.UseNat;
        _mtu = options.Mtu ?? _mtu;


        try {
            // create tun adapter
            Logger.LogInformation("Initializing TUN adapter...");
            await AdapterAdd(cancellationToken).VhConfigureAwait();

            // Private IP Networks
            Logger.LogDebug("Adding private networks...");
            if (options.VirtualIpNetworkV4 != null) {
                GatewayIpV4 = BuildGatewayFromFromNetwork(options.VirtualIpNetworkV4);
                await AddAddress(options.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();
            }

            if (options.VirtualIpNetworkV6 != null) {
                GatewayIpV6 = BuildGatewayFromFromNetwork(options.VirtualIpNetworkV6);
                await AddAddress(options.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // set metric
            Logger.LogDebug("Setting metric...");
            if (options.Metric != null) {
                await SetMetric(options.Metric.Value,
                    ipV4: options.VirtualIpNetworkV4 != null,
                    ipV6: options.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set mtu
            if (options.Mtu != null) {
                Logger.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: options.VirtualIpNetworkV4 != null,
                    ipV6: options.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set DNS servers
            Logger.LogDebug("Setting DNS servers...");
            var dnsServers = options.DnsServers;
            if (options.VirtualIpNetworkV4 == null)
                dnsServers = dnsServers.Where(x => !x.IsV4()).ToArray();
            if (options.VirtualIpNetworkV6 == null)
                dnsServers = dnsServers.Where(x => !x.IsV6()).ToArray();
            await SetDnsServers(dnsServers, cancellationToken).VhConfigureAwait();

            // add routes
            Logger.LogDebug("Adding routes...");
            foreach (var network in options.IncludeNetworks) {
                var gateway = network.IsV4 ? GatewayIpV4 : GatewayIpV6;
                if (gateway != null)
                    await AddRoute(network, gateway, cancellationToken).VhConfigureAwait();
            }

            // add NAT
            if (UseNat) {
                Logger.LogDebug("Adding NAT...");
                if (options.VirtualIpNetworkV4 != null && PrimaryAdapterIpV4 != null)
                    await AddNat(options.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();

                if (options.VirtualIpNetworkV6 != null && PrimaryAdapterIpV6 != null)
                    await AddNat(options.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // add app filter
            if (IsAppFilterSupported)
                await SetAppFilters(options.IncludeApps, options.ExcludeApps, cancellationToken);

            // open the adapter
            Logger.LogInformation("Initializing TUN adapter...");
            await AdapterOpen(cancellationToken).VhConfigureAwait();

            // start reading packets
            _ = Task.Run(ReadingPacketTask, CancellationToken.None);

            _started = true;
            Logger.LogInformation("TUN adapter started.");
        }
        catch (ExternalException ex) {
            Logger.LogError(ex, "Failed to start TUN adapter.");
            Stop();
            throw;
        }
    }

    private async Task SetAppFilters(string[]? includeApps, string[]? excludeApps, CancellationToken cancellationToken)
    {
        var appPackageId = AppPackageId;

        // validate the app filter
        if (appPackageId == null)
            throw new InvalidOperationException("AppPackageId must be available when AppFilter is supported.");

        if (!VhUtils.IsNullOrEmpty(includeApps) && !VhUtils.IsNullOrEmpty(excludeApps))
            throw new InvalidOperationException("Both include and exclude apps cannot be set at the same time.");

        // make sure current app is in the allowed list
        if (includeApps != null) {
            includeApps = includeApps.Concat([appPackageId]).Distinct().ToArray();
            await SetAllowedApps(includeApps, cancellationToken);
        }

        // make sure current app is not in the disallowed list
        if (excludeApps != null) {
            excludeApps = excludeApps.Where(x => x != appPackageId).Distinct().ToArray();
            await SetDisallowedApps(excludeApps, cancellationToken);
        }
    }

    private readonly object _stopLock = new();

    public void Stop()
    {
        lock (_stopLock) {

            if (_started) return;
            _started = false;

            Logger.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
            AdapterClose();
            AdapterRemove();

            PrimaryAdapterIpV4 = null;
            PrimaryAdapterIpV6 = null;
            AdapterIpNetworkV4 = null;
            AdapterIpNetworkV6 = null;
            GatewayIpV4 = null;
            GatewayIpV6 = null;

            Logger.LogInformation("TUN adapter stopped.");
        }
    }

    public virtual UdpClient CreateProtectedUdpClient(AddressFamily addressFamily)
    {
        if (CanProtectSocket) {
            var udpClient = new UdpClient(addressFamily);
            ProtectSocket(udpClient.Client);
            return udpClient;
        }

        return addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null =>
                new UdpClient(new IPEndPoint(PrimaryAdapterIpV4, 0)),

            AddressFamily.InterNetwork when PrimaryAdapterIpV4 == null =>
                new UdpClient(addressFamily),

            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null =>
                new UdpClient(new IPEndPoint(PrimaryAdapterIpV6, 0)),

            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 == null =>
                new UdpClient(addressFamily),

            _ => throw new NotSupportedException("The address family is not supported.")
        };
    }

    public virtual TcpClient CreateProtectedTcpClient(AddressFamily addressFamily)
    {
        if (CanProtectSocket) {
            var tcpClient = new TcpClient(addressFamily);
            ProtectSocket(tcpClient.Client);
            return tcpClient;
        }

        return addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null =>
                new TcpClient(new IPEndPoint(PrimaryAdapterIpV4, 0)),

            AddressFamily.InterNetwork when PrimaryAdapterIpV4 == null =>
                new TcpClient(addressFamily),

            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null =>
                new TcpClient(new IPEndPoint(PrimaryAdapterIpV6, 0)),

            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 == null =>
                new TcpClient(addressFamily),

            _ => throw new NotSupportedException("The address family is not supported.")
        };
    }

    private IPAddress? GetPrimaryAdapterIp(IPEndPoint remoteEndPoint)
    {
        try {
            using var udpClient = new UdpClient();
            udpClient.Connect(remoteEndPoint);
            return (udpClient.Client.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception) {
            Logger.LogDebug("Failed to get primary adapter IP. RemoteEndPoint: {RemoteEndPoint}",
                remoteEndPoint);
            return null;
        }
    }

    private static IPAddress? BuildGatewayFromFromNetwork(IpNetwork ipNetwork)
    {
        // Check for small subnets (IPv4: /31, /32 | IPv6: /128)
        return ipNetwork is { IsV4: true, PrefixLength: >= 31 } or { IsV6: true, PrefixLength: 128 }
            ? null
            : IPAddressUtil.Increment(ipNetwork.FirstIpAddress);
    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket); //todo
    }

    public void SendPacketToInbound(IList<IPPacket> ipPackets)
    {
        SendPacket(ipPackets); //todo
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

        try {
            SendPacketInternal(ipPacket);
            _autoRestartCount = 0;
        }
        catch (Exception ex) {
            if (_autoRestartCount < _maxAutoRestartCount) {
                Logger.LogError(ex, "Failed to send packet via TUN adapter.");
                RestartAdapter();
            }
        }
    }

    private void SendPacketInternal(IPPacket ipPacket)
    {
        // try to send the packet with exponential backoff
        var sent = false;
        var delay = 5;
        while (true) {
            // break if the packet is sent
            if (WritePacket(ipPacket)) {
                sent = true;
                break;
            }

            // break if delay exceeds the max delay
            if (delay > _maxPacketSendDelayMs)
                break;

            // wait for the next try
            delay *= 2;
            Task.Delay(delay).Wait();
            WaitForTunWrite();
        }

        // log if failed to send
        if (!sent)
            Logger.LogWarning("Failed to send packet via WinTun adapter.");
    }

    public void SendPacket(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    private readonly SemaphoreSlim _sendPacketSemaphore = new(1, 1);

    public Task SendPacketAsync(IPPacket ipPacket)
    {
        return _sendPacketSemaphore.WaitAsync().ContinueWith(_ => {
            try {
                SendPacket(ipPacket);
            }
            finally {
                _sendPacketSemaphore.Release();
            }
        });
    }

    public async Task SendPacketAsync(IList<IPPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            await SendPacketAsync(ipPacket).VhConfigureAwait();

    }

    private void ReadingPacketTask()
    {
        var packetList = new List<IPPacket>(adapterSettings.MaxPacketCount);

        // Read packets from TUN adapter
        while (Started) {
            try {
                // read packets from the adapter until the list is full
                while (packetList.Count < packetList.Capacity) {
                    var packet = ReadPacket(_mtu);
                    if (packet == null)
                        break;

                    // add the packets to the list
                    packetList.Add(packet);
                    _autoRestartCount = 0; // reset the auto restart count
                }

                // break if the adapter is stopped or disposed
                if (!Started)
                    break;

                // invoke the packet received event
                if (packetList.Count > 0)
                    InvokeReadPackets(packetList);
                else
                    WaitForTunRead();
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Error in reading packets from TUN adapter.");
                if (!Started || _autoRestartCount >= _maxAutoRestartCount)
                    break;
                RestartAdapter();
            }
        }

        // stop the adapter if it is not stopped
        Stop();
    }

    private void RestartAdapter()
    {
        Logger.LogWarning("Restarting the adapter. RestartCount: {RestartCount}", _autoRestartCount + 1);

        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (!Started)
            throw new InvalidOperationException("Cannot restart the adapter when it is stopped.");

        _autoRestartCount++;
        AdapterClose();
        AdapterOpen(CancellationToken.None);
    }

    private void InvokeReadPackets(List<IPPacket> packetList)
    {
        try {
            if (packetList.Count > 0)
                PacketReceived?.Invoke(this, new PacketReceivedEventArgs(packetList));
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error in invoking packet received event.");
        }
        finally {
            packetList.Clear();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        IsDisposed = true;

        // release managed resources when disposing
        if (disposing) {
            Stop();

            // notify the subscribers that the adapter is disposed
            Disposed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TunVpnAdapter()
    {
        Dispose(false);
    }
}