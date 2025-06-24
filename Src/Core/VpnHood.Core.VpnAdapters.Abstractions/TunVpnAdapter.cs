using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public abstract class TunVpnAdapter : PacketTransport, IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs;
    private const int MaxIoErrorCount = 10;
    private readonly bool _autoRestart;
    private int _mtu = 0xFFFF;
    private int _ioErrorCount;
    private readonly bool _autoMetric;
    private static readonly IpNetwork[] WebDeadNetworks = [IpNetwork.Parse("203.0.113.1/24"), IpNetwork.Parse("2001:4860:ffff::1234/48")];
    private readonly object _stopLock = new();
    private bool _isRestarting;
    private bool _isStopping;
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
    /// <summary>
    /// Return false if there is no packet
    /// </summary>
    /// <returns></returns>
    protected abstract bool ReadPacket(byte[] buffer);
    protected abstract bool WritePacket(IpPacket ipPacket);

    public event EventHandler? Disposed;
    public string AdapterName { get; }
    public IPAddress? PrimaryAdapterIpV4 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
    public IPAddress? PrimaryAdapterIpV6 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }
    public IPAddress? GatewayIpV4 { get; private set; }
    public IPAddress? GatewayIpV6 { get; private set; }
    public bool IsIpVersionSupported(IpVersion ipVersion) => GetPrimaryAdapterAddress(ipVersion) != null;
    public bool IsStarted { get; private set; }

    // ReSharper disable once InconsistentlySynchronizedField
    private bool IsReady => IsStarted && !_isStopping && !IsDisposed && !IsDisposing;

    protected TunVpnAdapter(VpnAdapterSettings adapterSettings)
        : base(adapterSettings)
    {
        _maxPacketSendDelayMs = (int)adapterSettings.MaxPacketSendDelay.TotalMilliseconds;
        _autoRestart = adapterSettings.AutoRestart;
        _autoMetric = adapterSettings.AutoMetric;
        AdapterName = adapterSettings.AdapterName;
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        // Do not update the primary adapter IPs if the adapter is stopping or disposed
        // it will be updated on the next start
        if (_isStopping || IsDisposed || IsDisposing)
            return;

        PrimaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
        PrimaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
    }

    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? PrimaryAdapterIpV4 : PrimaryAdapterIpV6;
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (UseNat && !IsNatSupported)
            throw new NotSupportedException("NAT is not supported by this adapter.");

        try {
            VhLogger.Instance.LogInformation("Starting the VPN adapter. AdapterName: {AdapterName}", AdapterName);

            // We must set the started at first, to let clean-up be done stop via any exception. 
            // We hope client await the start otherwise we need different state for adapters
            IsStarted = true;

            // get the WAN adapter IP (lets do it again)
            PrimaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
            PrimaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
            AdapterIpNetworkV4 = options.VirtualIpNetworkV4;
            AdapterIpNetworkV6 = options.VirtualIpNetworkV6;
            UseNat = options.UseNat;
            _mtu = options.Mtu ?? _mtu;

            // report the primary adapter IPs
            VhLogger.Instance.LogInformation(
                "TunAdapterInfo. AdapterType: {AdapterType}, UseNat: {UseNat}, MTU: {MTU}, " +
                "PrimaryAdapterIpV4: {PrimaryAdapterIpV4}, PrimaryAdapterIpV6: {PrimaryAdapterIpV6}, " +
                "AdapterIpNetworkV4: {AdapterIpNetworkV4}, AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                VhLogger.FormatType(this), UseNat, _mtu,
                VhLogger.Format(PrimaryAdapterIpV4), VhLogger.Format(PrimaryAdapterIpV6),
                AdapterIpNetworkV4, AdapterIpNetworkV6);

            // create tun adapter
            VhLogger.Instance.LogInformation("Adding TUN adapter...");
            await AdapterAdd(cancellationToken).Vhc();

            // Set adapter IPv4 address
            if (AdapterIpNetworkV4 != null) {
                VhLogger.Instance.LogDebug("Adding IPv4 address to adapter ...");
                GatewayIpV4 = BuildGatewayFromFromNetwork(AdapterIpNetworkV4);
                await AddAddress(AdapterIpNetworkV4, cancellationToken).Vhc();
            }

            // Set adapter IPv6 address
            if (AdapterIpNetworkV6 != null) {
                VhLogger.Instance.LogDebug("Adding IPv6 address to adapter ...");
                try {
                    GatewayIpV6 = BuildGatewayFromFromNetwork(AdapterIpNetworkV6);
                    await AddAddress(AdapterIpNetworkV6, cancellationToken).Vhc();
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
                    cancellationToken).Vhc();
            }

            // set mtu
            if (options.Mtu != null) {
                VhLogger.Instance.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: AdapterIpNetworkV4 != null,
                    ipV6: AdapterIpNetworkV6 != null,
                    cancellationToken).Vhc();
            }

            // set DNS servers
            VhLogger.Instance.LogDebug("Setting DNS servers...");
            var dnsServers = options.DnsServers;
            if (AdapterIpNetworkV4 == null)
                dnsServers = dnsServers.Where(x => !x.IsV4()).ToArray();
            if (AdapterIpNetworkV6 == null)
                dnsServers = dnsServers.Where(x => !x.IsV6()).ToArray();
            await SetDnsServers(dnsServers, cancellationToken).Vhc();

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
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetwork, cancellationToken).Vhc();
            if (AdapterIpNetworkV6 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetworkV6, cancellationToken).Vhc();

            // add NAT
            if (UseNat) {
                VhLogger.Instance.LogDebug("Adding NAT...");
                if (AdapterIpNetworkV4 != null && PrimaryAdapterIpV4 != null)
                    await AddNat(AdapterIpNetworkV4, cancellationToken).Vhc();

                if (AdapterIpNetworkV6 != null && PrimaryAdapterIpV6 != null)
                    await AddNat(AdapterIpNetworkV6, cancellationToken).Vhc();
            }

            // add app filter
            if (IsAppFilterSupported)
                await SetAppFilters(options.IncludeApps, options.ExcludeApps, cancellationToken);

            // open the adapter
            VhLogger.Instance.LogInformation("Opening TUN adapter...");
            await AdapterOpen(cancellationToken).Vhc();

            // start reading packets
            _ = Task.Run(StartReadingPackets, CancellationToken.None);

            VhLogger.Instance.LogInformation("TUN adapter started.");
        }
        catch (Exception ex) {
            VhLogger.Instance.Log(ex is OperationCanceledException ? LogLevel.Trace : LogLevel.Error, ex, 
                "Failed to start TUN adapter.");
            Stop(false);
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
            await AddRoute(network, cancellationToken).Vhc();
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        Stop(throwException: true);
    }

    private void Stop(bool throwException)
    {
        lock (_stopLock) {
            if (!IsStarted || _isStopping)
                return;

            try {
                VhLogger.Instance.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
                _isStopping = true;
                AdapterClose();
                AdapterRemove();

                PrimaryAdapterIpV4 = null;
                PrimaryAdapterIpV6 = null;
                AdapterIpNetworkV4 = null;
                AdapterIpNetworkV6 = null;
                GatewayIpV4 = null;
                GatewayIpV6 = null;
                IsStarted = false;
                VhLogger.Instance.LogInformation("TUN adapter stopped.");
            }
            catch (Exception ex) {
                if (throwException)
                    throw;
                // log exception if it does not throw
                VhLogger.Instance.LogError(ex, "Failed to stop the TUN adapter. AdapterName: {AdapterName}",
                    AdapterName);
            }
            finally {
                _isStopping = false;
            }
        }
    }

    protected void BindToAny(Socket socket)
    {
        var ipAddress = socket.AddressFamily.IsV4() ? IPAddress.Any : IPAddress.IPv6Any;
        socket.Bind(new IPEndPoint(ipAddress, 0));
    }

    public virtual bool ProtectSocket(Socket socket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
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
            var localEndPoint = udpClient.Client.GetLocalEndPoint();

            // log the discovered primary adapter IP
            VhLogger.Instance.LogDebug(
                "Primary adapter IP discovered. PrimaryAdapterIp: {PrimaryAdapterIp}",
                VhLogger.Format(localEndPoint.Address));

            return localEndPoint.Address;
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

    protected override ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++)
            SendPacket(ipPackets[i]);

        return default;
    }

    protected void SendPacket(IpPacket ipPacket)
    {
        if (!IsReady)
            throw new InvalidOperationException("TUN adapter is not in ready state.");

        try {
            SendPacketInternal(ipPacket);
            _ioErrorCount = 0;
        }
        catch (Exception) when (!_isRestarting) {
            _ioErrorCount++;
            if (_ioErrorCount < MaxIoErrorCount || _isRestarting)
                throw;

            // restart if the maximum I/O error count is exceeded
            if (_autoRestart) {
                _ = RestartAdapter();
                throw;
            }

            // stop the adapter if not restarting
            Stop(false);
            throw;
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

    protected virtual void StartReadingPackets()
    {
        // Read packets from TUN adapter
        while (IsReady) {
            try {
                // read next packet
                var packet = ReadPacket(_mtu);

                // reset error counters if not exception
                _ioErrorCount = 0;

                // no packet available, wait before retrying
                if (packet == null) {
                    WaitForTunRead();
                    continue;
                }

                // process the packet
                OnPacketReceived(packet);
            }
            catch (Exception) when (!IsReady) {
                break; // normal stop
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in reading packets from TUN adapter.");
                _ioErrorCount++;
                if (_ioErrorCount < MaxIoErrorCount || _isRestarting)
                    continue;

                // restart if the maximum I/O error count is exceeded
                if (_autoRestart) {
                    _ = RestartAdapter();
                    continue;
                }

                // stop the adapter if not restarting
                break;
            }
        }

        // stop the adapter if it is not stopped
        VhLogger.Instance.LogDebug("Finish reading the packets from the TUN adapter.");
        Stop(false);
    }

    protected virtual IpPacket? ReadPacket(int mtu)
    {
        // Allocate a memory block for the packet
        var memoryOwner = MemoryPool<byte>.Shared.Rent(mtu);

        // Get the underlying array from the memory owner
        if (!MemoryMarshal.TryGetArray<byte>(memoryOwner.Memory, out var segment))
            throw new InvalidOperationException("Could not get array from memory owner.");

        try {
            if (segment.Array == null)
                throw new InvalidOperationException("Memory owner's segment returned a null array.");

            // read packet
            var success = ReadPacket(segment.Array);
            if (success)
                return PacketBuilder.Attach(memoryOwner);

            // no more packet
            memoryOwner.Dispose();
            return null;
        }
        catch {
            memoryOwner.Dispose();
            throw;
        }
    }

    private async Task RestartAdapter()
    {
        VhLogger.Instance.LogWarning("Restarting the adapter.");
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!IsStarted)
            throw new InvalidOperationException("Cannot restart the adapter when it is stopped.");

        _isRestarting = false;
        try {
            AdapterClose();
            await AdapterOpen(CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to restart the adapter.");
            throw;
        }
        finally {
            _isRestarting = false;
        }
    }

    protected sealed override void PreDispose()
    {
        Stop(false);
        base.PreDispose();
    }

    protected override void DisposeManaged()
    {
        // notify the subscribers that the adapter is disposed
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        Disposed?.Invoke(this, EventArgs.Empty);
        Disposed = null;

        base.DisposeManaged();
    }
}