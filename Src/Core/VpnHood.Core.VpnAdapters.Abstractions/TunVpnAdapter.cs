using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Transports;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public abstract class TunVpnAdapter : IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs;
    private int _mtu = 0xFFFF;
    private readonly int _maxAutoRestartCount;
    private int _autoRestartCount;
    private readonly PacketReceivedEventArgs _packetReceivedEventArgs = new([]);
    private readonly SemaphoreSlim _sendPacketSemaphore = new(1, 1);
    private readonly bool _autoMetric;
    private static readonly IpNetwork[] WebDeadNetworks = [IpNetwork.Parse("203.0.113.1/24"), IpNetwork.Parse("2001:4860:ffff::1234/48")];

    protected bool IsDisposed { get; private set; }
    protected bool UseNat { get; private set; }
    public abstract bool IsAppFilterSupported { get; }
    public abstract bool IsNatSupported { get; }
    public virtual bool CanProtectSocket => true;
    protected abstract bool IsSocketProtectedByBind { get; }
    protected abstract string? AppPackageId { get; }
    protected abstract Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken);
    protected abstract Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken);
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
    protected abstract IpPacket? ReadPacket(int mtu);
    protected abstract bool WritePacket(IpPacket ipPacket);

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler? Disposed;
    public string AdapterName { get; }
    public IPAddress? PrimaryAdapterIpV4 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
    public IPAddress? PrimaryAdapterIpV6 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }
    public IPAddress? GatewayIpV4 { get; private set; }
    public IPAddress? GatewayIpV6 { get; private set; }
    public bool IsIpVersionSupported(IpVersion ipVersion) => GetPrimaryAdapterAddress(ipVersion) != null;
    public bool Started { get; private set; }

    private readonly object _stopLock = new();
    private readonly VpnAdapterSettings _adapterSettings;

    protected TunVpnAdapter(VpnAdapterSettings adapterSettings)
    {
        _adapterSettings = adapterSettings;
        _maxPacketSendDelayMs = (int)adapterSettings.MaxPacketSendDelay.TotalMilliseconds;
        _maxAutoRestartCount = adapterSettings.MaxAutoRestartCount;
        _autoMetric = adapterSettings.AutoMetric;
        AdapterName = adapterSettings.AdapterName;
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
    }

    private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
    {
        PrimaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
        PrimaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
    }

    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? PrimaryAdapterIpV4 : PrimaryAdapterIpV6;
    }

    public IPAddress? GetPrimaryAdapterAddress(AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork => GetPrimaryAdapterAddress(IpVersion.IPv4),
            AddressFamily.InterNetworkV6 => GetPrimaryAdapterAddress(IpVersion.IPv6),
            _ => throw new NotSupportedException("Address family is not supported.")
        };
    }

    public IPAddress? GetGatewayIp(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? GatewayIpV4 : GatewayIpV6;
    }

    public IpNetwork? GetIpNetwork(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? AdapterIpNetworkV4 : AdapterIpNetworkV6;
    }

    public async Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (UseNat && !IsNatSupported)
            throw new NotSupportedException("NAT is not supported by this adapter.");

        try {
            VhLogger.Instance.LogInformation("Starting the VPN adapter. AdapterName: {AdapterName}", AdapterName);

            // We must set the started at first, to let clean-up be done stop via any exception. 
            // We hope client await the start otherwise we need different state for adapters
            Started = true;

            // get the WAN adapter IP (lets do it again)
            PrimaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
            PrimaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
            AdapterIpNetworkV4 = options.VirtualIpNetworkV4;
            AdapterIpNetworkV6 = options.VirtualIpNetworkV6;
            UseNat = options.UseNat;
            _mtu = options.Mtu ?? _mtu;

            // create tun adapter
            VhLogger.Instance.LogInformation("Adding TUN adapter...");
            await AdapterAdd(cancellationToken).VhConfigureAwait();

            // Set adapter IPv4 address
            if (AdapterIpNetworkV4 != null) {
                VhLogger.Instance.LogDebug("Adding IPv4 address to adapter ...");
                GatewayIpV4 = BuildGatewayFromFromNetwork(AdapterIpNetworkV4);
                await AddAddress(AdapterIpNetworkV4, cancellationToken).VhConfigureAwait();
            }

            // Set adapter IPv6 address
            if (AdapterIpNetworkV6 != null) {
                VhLogger.Instance.LogDebug("Adding IPv6 address to adapter ...");
                try {
                    GatewayIpV6 = BuildGatewayFromFromNetwork(AdapterIpNetworkV6);
                    await AddAddress(AdapterIpNetworkV6, cancellationToken).VhConfigureAwait();
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex,
                        "Failed to add IPv6 address to TUN adapter. AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                        AdapterIpNetworkV6);
                    AdapterIpNetworkV6 = null;
                }
            }

            // set metric
            if (options.Metric != null) {
                VhLogger.Instance.LogDebug("Setting metric...");
                await SetMetric(options.Metric.Value,
                    ipV4: AdapterIpNetworkV4 != null,
                    ipV6: AdapterIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set mtu
            if (options.Mtu != null) {
                VhLogger.Instance.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: AdapterIpNetworkV4 != null,
                    ipV6: AdapterIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set DNS servers
            VhLogger.Instance.LogDebug("Setting DNS servers...");
            var dnsServers = options.DnsServers;
            if (AdapterIpNetworkV4 == null)
                dnsServers = dnsServers.Where(x => !x.IsV4()).ToArray();
            if (AdapterIpNetworkV6 == null)
                dnsServers = dnsServers.Where(x => !x.IsV6()).ToArray();
            await SetDnsServers(dnsServers, cancellationToken).VhConfigureAwait();

            // exclude dead networks
            var includeNetworks = options.IncludeNetworks;
            if (IsSocketProtectedByBind) {
                includeNetworks = includeNetworks
                    .ToIpRanges()
                    .Exclude(WebDeadNetworks.ToIpRanges())
                    .ToIpNetworks()
                    .ToArray();
            }

            // add routes
            VhLogger.Instance.LogDebug("Adding routes...");
            if (AdapterIpNetworkV4 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetwork, cancellationToken).VhConfigureAwait();
            if (AdapterIpNetworkV6 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetworkV6, cancellationToken).VhConfigureAwait();

            // add NAT
            if (UseNat) {
                VhLogger.Instance.LogDebug("Adding NAT...");
                if (AdapterIpNetworkV4 != null && PrimaryAdapterIpV4 != null)
                    await AddNat(AdapterIpNetworkV4, cancellationToken).VhConfigureAwait();

                if (AdapterIpNetworkV6 != null && PrimaryAdapterIpV6 != null)
                    await AddNat(AdapterIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // add app filter
            if (IsAppFilterSupported)
                await SetAppFilters(options.IncludeApps, options.ExcludeApps, cancellationToken);

            // open the adapter
            VhLogger.Instance.LogInformation("Opening TUN adapter...");
            await AdapterOpen(cancellationToken).VhConfigureAwait();

            // start reading packets
            _ = Task.Run(StartReadingPackets, CancellationToken.None);

            VhLogger.Instance.LogInformation("TUN adapter started.");
        }
        catch (ExternalException ex) {
            VhLogger.Instance.LogError(ex, "Failed to start TUN adapter.");
            Stop();
            throw;
        }
    }

    private async Task AddRouteHelper(IEnumerable<IpNetwork> ipNetworks, AddressFamily addressFamily, CancellationToken cancellationToken)
    {
        // remove the local networks
        ipNetworks = ipNetworks.Where(x => x.AddressFamily == addressFamily);

        if (_autoMetric) {
            // ReSharper disable once PossibleMultipleEnumeration
            var sortedIpNetworks = ipNetworks.Sort();

            // ReSharper disable once PossibleMultipleEnumeration
            if (addressFamily.IsV4() && sortedIpNetworks.IsAllV4())
                ipNetworks = VpnAdapterOptions.AllVRoutesIpV4;

            // ReSharper disable once PossibleMultipleEnumeration
            if (addressFamily.IsV6() && sortedIpNetworks.IsAllV6())
                ipNetworks = VpnAdapterOptions.AllVRoutesIpV6;
        }

        // ReSharper disable once PossibleMultipleEnumeration
        foreach (var network in ipNetworks)
            await AddRoute(network, cancellationToken).VhConfigureAwait();
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

    public void Stop()
    {
        lock (_stopLock) {

            if (Started) return;

            VhLogger.Instance.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
            AdapterClose();
            AdapterRemove();

            PrimaryAdapterIpV4 = null;
            PrimaryAdapterIpV6 = null;
            AdapterIpNetworkV4 = null;
            AdapterIpNetworkV6 = null;
            GatewayIpV4 = null;
            GatewayIpV6 = null;
            Started = false;
            VhLogger.Instance.LogInformation("TUN adapter stopped.");
        }
    }

    protected void BindToAny(Socket socket)
    {
        var ipAddress = socket.AddressFamily.IsV4() ? IPAddress.Any : IPAddress.IPv6Any;
        socket.Bind(new IPEndPoint(ipAddress, 0));
    }

    public virtual bool ProtectSocket(Socket socket)
    {
        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily);
        if (primaryAdapterIp == null) {
            BindToAny(socket);
            return false;
        }

        // bind the socket to the primary adapter IP
        socket.Bind(new IPEndPoint(primaryAdapterIp, 0));
        return true;
    }

    public virtual bool ProtectSocket(Socket socket, IPAddress remoteAddress)
    {
        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily);
        if (primaryAdapterIp == null) {
            BindToAny(socket);
            return false;
        }

        // could not protect loopback addresses or not needed at all, because loopback can not be routed
        if (IPAddress.IsLoopback(primaryAdapterIp) != IPAddress.IsLoopback(remoteAddress)) {
            BindToAny(socket);
            return false;
        }

        // bind the socket to the primary adapter IP and connect to the remote endpoint
        socket.Bind(new IPEndPoint(primaryAdapterIp, 0));
        return true;
    }

    private static IPAddress? DiscoverPrimaryAdapterIp(AddressFamily addressFamily)
    {
        // not matter is it reachable or not, just try to get the primary adapter IP which can route to the internet
        var ipAddress = WebDeadNetworks.First(x => x.AddressFamily == addressFamily).Prefix;
        var remoteEndPoint = new IPEndPoint(ipAddress, 53);

        try {
            // IPv6 needs the addressFamily to be set
            using var udpClient = new UdpClient(addressFamily);
            udpClient.Connect(remoteEndPoint);
            return (udpClient.Client.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception) {
            VhLogger.Instance.LogDebug("Failed to get primary adapter IP. RemoteEndPoint: {RemoteEndPoint}",
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

    private readonly object _sendLock = new();
    public void SendPacket(IpPacket ipPacket)
    {
        if (!Started)
            throw new InvalidOperationException("TUN adapter is not started.");

        try {
            lock (_sendLock)
                SendPacketInternal(ipPacket);

            _autoRestartCount = 0;
        }
        catch (Exception ex) {
            if (_autoRestartCount < _maxAutoRestartCount) {
                VhLogger.Instance.LogError(ex, "Failed to send packet via TUN adapter.");
                RestartAdapter();
            }
        }
    }

    private void SendPacketInternal(IpPacket ipPacket)
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
            VhLogger.Instance.LogWarning("Failed to send packet via WinTun adapter.");
    }

    public void SendPackets(IList<IpPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    public Task SendPacketAsync(IpPacket ipPacket)
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

    public Task SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        return _sendPacketSemaphore.WaitAsync().ContinueWith(_ => {
            try {
                SendPackets(ipPackets);
            }
            finally {
                _sendPacketSemaphore.Release();
            }
        });
    }

    protected virtual void StartReadingPackets()
    {
        var packetList = new List<IpPacket>(_adapterSettings.MaxPacketCount);

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
            catch (Exception) when (!Started || IsDisposed) {
                break; // normal stop
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in reading packets from TUN adapter.");
                if (_autoRestartCount >= _maxAutoRestartCount)
                    break;

                RestartAdapter();
            }
        }

        // stop the adapter if it is not stopped
        VhLogger.Instance.LogDebug("Finish reading the packets from the TUN adapter.");
        Stop();
    }

    private void RestartAdapter()
    {
        VhLogger.Instance.LogWarning("Restarting the adapter. RestartCount: {RestartCount}", _autoRestartCount + 1);

        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (!Started)
            throw new InvalidOperationException("Cannot restart the adapter when it is stopped.");

        _autoRestartCount++;
        AdapterClose();
        AdapterOpen(CancellationToken.None);
    }

    protected void InvokeReadPackets(IList<IpPacket> packetList)
    {
        try {
            if (packetList.Count > 0) {
                _packetReceivedEventArgs.IpPackets = packetList;
                PacketReceived?.Invoke(this, _packetReceivedEventArgs);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in invoking packet received event.");
        }
        finally {
            if (!packetList.IsReadOnly)
                packetList.Clear();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        // release managed resources when disposing
        if (disposing) {
            IsDisposed = true;
            Stop();

            // notify the subscribers that the adapter is disposed
            Disposed?.Invoke(this, EventArgs.Empty);
            PacketReceived = null;
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        }

        IsDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TunVpnAdapter()
    {
        Dispose(false);
    }
}