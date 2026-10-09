using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.VpnAdapters.Abstractions;

public abstract class TunVpnAdapter : PacketTransport, IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs;
    private const int MaxIoErrorCount = 10;
    private readonly bool _autoRestart;
    private int _mtu = 0xFFFF;
    private int _ioErrorCount;
    private readonly bool _autoMetric;
    private HashSet<IPAddress> _lastPrimaryAdapterAddresses = [];

    private static readonly IpNetwork[] WebDeadNetworks =
        [IpNetwork.Parse("203.0.113.1/24"), IpNetwork.Parse("2001:4860:ffff::1234/48")];

    private readonly Lock _stopLock = new();
    private bool _isStarting;
    private bool _isStopping;
    private VpnAdapterOptions? _startOptions;

    // The owner wants the adapter started: set by its Start, cleared by its Stop and Dispose. A restart
    // and a retry start nothing without it, and its source cancels the ones under way. The source is
    // replaced per start and never disposed: it has no timer, and a token in flight may still read it.
    private volatile bool _keepStarted;
    private CancellationTokenSource _keepStartedCts = new();

    // This run's reader, which a stop may wait for (WaitForReader), and the run it reads for, by its task's
    // id: a stop retires it, so a reader that outlives the stop leaves instead of reading the next run's
    // session. The sender is waited for by the transport's own sending flag (WaitForSender), so nothing
    // is added per batch; only the adapter whose sender is stopping it on its own errors is marked, on
    // that thread.
    private volatile Task _readerTask = Task.CompletedTask;
    private volatile int _readerRunId;
    private volatile int _readerThreadId;
    [ThreadStatic] private static TunVpnAdapter? _senderErrorStopAdapter;

    protected IPAddress? ServerIp => _startOptions?.ServerIp;

    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    protected bool UseNat { get; private set; }
    public abstract bool IsAppFilterSupported { get; }
    public abstract bool IsNatSupported(IpVersion ipVersion);
    public virtual bool CanProtectSocket => true;
    protected abstract bool IsSocketProtectedByBind { get; }
    protected abstract string? AppPackageId { get; }
    protected abstract Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken);
    protected abstract Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task SetSessionName(string sessionName, CancellationToken cancellationToken);
    protected abstract Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken);
    protected abstract Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken);
    protected abstract Task AdapterAdd(CancellationToken cancellationToken);
    protected abstract void AdapterRemove();
    protected abstract Task AdapterOpen(CancellationToken cancellationToken);
    protected abstract void AdapterClose();
    protected abstract void WaitForTunWrite();
    protected abstract void WaitForTunRead();
    protected abstract bool RestartAfterNetworkAddressChanged { get; }

    // the wait before AutoRestart tries a failed restart again
    protected virtual TimeSpan AutoRestartDelay => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Return false if there is no packet
    /// </summary>
    /// <returns></returns>
    protected abstract bool ReadPacket(byte[] buffer);

    protected abstract bool WritePacket(IpPacket ipPacket);


    public event EventHandler? Disposed;
    public event EventHandler<Exception>? Failed;
    public string AdapterName { get; }
    public IPAddress? PrimaryAdapterIpV4 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
    public IPAddress? PrimaryAdapterIpV6 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }
    public IPAddress? GatewayIpV4 { get; private set; }
    public IPAddress? GatewayIpV6 { get; private set; }
    /// <summary>
    /// Checks if the physical device network/primary adapter supports the specified IP version.
    /// </summary>
    /// <remarks>
    /// This is used to determine if the primary physical interface has connectivity/IP configuration
    /// for the given IP version. It is typically used for routing and split-tunneling decisions,
    /// and to check if the channel to the server can be established using this IP version.
    /// It does NOT represent the capabilities of the virtual VPN adapter inside the tunnel.
    /// </remarks>
    public virtual bool IsIpVersionSupported(IpVersion ipVersion) => GetPrimaryAdapterAddress(ipVersion) != null;
    public bool IsStarted { get; private set; }
    public event EventHandler? PrimaryAdapterIpChanged;

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
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
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
        ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);

        // the owner's start: from here the adapter keeps itself started, until the owner's Stop or Dispose
        _keepStarted = true;
        _keepStartedCts = new CancellationTokenSource();
        try {
            await StartCore(options, cancellationToken).Vhc();
        }
        catch {
            _keepStarted = false; // the caller has the exception: nothing to keep started
            throw;
        }

        // an address change during the start was left to it: see it now
        CheckPrimaryAdapterAddresses();
    }

    // The start itself, for the owner's Start and for a restart
    private async Task StartCore(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        _startOptions = options;

        if (options.UseNat && !IsNatSupported(IpVersion.IPv4))
            throw new NotSupportedException("NAT is not supported by this adapter.");

        try {
            VhLogger.Instance.LogInformation("Starting the VPN adapter. AdapterName: {AdapterName}", AdapterName);

            // We must set the started at first, to let clean-up be done stop via any exception.
            // We hope client await the start otherwise we need different state for adapters
            _isStarting = true;
            lock (_stopLock) {
                // a disposal or a stop that got here first found nothing to stop, so nothing may start after it
                ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);
                if (!_keepStarted)
                    throw new OperationCanceledException("The VPN adapter has been stopped while starting.");
                IsStarted = true;
            }
            _ioErrorCount = 0; // a new run, not the errors that ended the last one

            // get the WAN adapter IP (lets do it again)
            // A concurrent Stop() clears the adapter properties when the user disconnects mid-start,
            // so the rest of this method must read these locals, never the properties.
            var primaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
            var primaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
            var adapterIpNetworkV4 = options.VirtualIpNetworkV4;
            var adapterIpNetworkV6 = options.VirtualIpNetworkV6;
            PrimaryAdapterIpV4 = primaryAdapterIpV4;
            PrimaryAdapterIpV6 = primaryAdapterIpV6;
            AdapterIpNetworkV4 = adapterIpNetworkV4;
            AdapterIpNetworkV6 = adapterIpNetworkV6;
            UseNat = options.UseNat;
            _mtu = options.Mtu ?? _mtu;
            _lastPrimaryAdapterAddresses = GetPrimaryAdapterAddresses();

            // report the primary adapter IPs
            VhLogger.Instance.LogInformation(
                "TunAdapterInfo. AdapterType: {AdapterType}, UseNat: {UseNat}, MTU: {MTU}, " +
                "PrimaryAdapterIpV4: {PrimaryAdapterIpV4}, PrimaryAdapterIpV6: {PrimaryAdapterIpV6}, " +
                "AdapterIpNetworkV4: {AdapterIpNetworkV4}, AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                VhLogger.FormatType(this), UseNat, _mtu,
                VhLogger.Format(primaryAdapterIpV4), VhLogger.Format(primaryAdapterIpV6),
                adapterIpNetworkV4, adapterIpNetworkV6);

            // create tun adapter
            VhLogger.Instance.LogInformation("Adding TUN adapter...");
            await AdapterAdd(cancellationToken).Vhc();

            // SetSessionName
            if (!string.IsNullOrEmpty(options.SessionName))
                await SetSessionName(options.SessionName, cancellationToken);

            // Set adapter IPv4 address
            if (adapterIpNetworkV4 != null) {
                VhLogger.Instance.LogDebug("Adding IPv4 address to adapter ...");
                GatewayIpV4 = BuildGatewayFromFromNetwork(adapterIpNetworkV4);
                await AddAddress(adapterIpNetworkV4, cancellationToken).Vhc();
            }

            // Set adapter IPv6 address
            if (adapterIpNetworkV6 != null) {
                VhLogger.Instance.LogDebug("Adding IPv6 address to adapter ...");
                try {
                    GatewayIpV6 = BuildGatewayFromFromNetwork(adapterIpNetworkV6);
                    await AddAddress(adapterIpNetworkV6, cancellationToken).Vhc();
                }
                // a cancellation is no IPv6 failure: it passes to the start's own catch, which stops the adapter
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex,
                        "Failed to add IPv6 address to TUN adapter. AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                        adapterIpNetworkV6);
                    adapterIpNetworkV6 = null;
                    AdapterIpNetworkV6 = null;
                }
            }

            // set metric
            if (options.Metric != null) {
                VhLogger.Instance.LogDebug("Setting metric...");
                await SetMetric(options.Metric.Value,
                    ipV4: adapterIpNetworkV4 != null,
                    ipV6: adapterIpNetworkV6 != null,
                    cancellationToken).Vhc();
            }

            // set mtu
            if (options.Mtu != null) {
                VhLogger.Instance.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: adapterIpNetworkV4 != null,
                    ipV6: adapterIpNetworkV6 != null,
                    cancellationToken).Vhc();
            }

            // set DNS servers
            VhLogger.Instance.LogDebug("Setting DNS servers...");
            var dnsServers = options.DnsServers;
            if (adapterIpNetworkV4 == null)
                dnsServers = dnsServers.Where(x => !x.IsV4()).ToArray();
            if (adapterIpNetworkV6 == null)
                dnsServers = dnsServers.Where(x => !x.IsV6()).ToArray();
            await SetDnsServers(dnsServers, cancellationToken).Vhc();

            // exclude dead networks
            var includeNetworks = options.IncludeNetworks.ToArray();
            if (IsSocketProtectedByBind) {
                includeNetworks = includeNetworks
                    .ToIpRanges()
                    .Exclude(WebDeadNetworks.ToIpRanges())
                    .ToIpNetworks()
                    .ToArray();
            }

            // add routes
            VhLogger.Instance.LogDebug("Adding routes...");
            if (adapterIpNetworkV4 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetwork, cancellationToken).Vhc();
            if (adapterIpNetworkV6 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetworkV6, cancellationToken).Vhc();

            // add NAT, for each IP version the adapter's NAT translates
            if (UseNat) {
                VhLogger.Instance.LogDebug("Adding NAT...");
                if (adapterIpNetworkV4 != null && primaryAdapterIpV4 != null && IsNatSupported(IpVersion.IPv4))
                    await AddNat(adapterIpNetworkV4, cancellationToken).Vhc();

                if (adapterIpNetworkV6 != null && primaryAdapterIpV6 != null && IsNatSupported(IpVersion.IPv6))
                    await AddNat(adapterIpNetworkV6, cancellationToken).Vhc();
            }

            // add app filter
            if (IsAppFilterSupported)
                await SetAppFilters(options.IncludeApps, options.ExcludeApps, cancellationToken);

            // open the adapter
            VhLogger.Instance.LogInformation("Opening TUN adapter...");
            await AdapterOpen(cancellationToken).Vhc();

            // start reading packets; the task is published before it runs, so a stop from here waits
            // for this run's reader, not the last one's. Under the stop lock: a stop that overtook this
            // start has retired its run, and no reader may start after it. On the default scheduler, as
            // Task.Run: never on a caller's own scheduler, which the blocking reader would hold
            var readerTask = new Task(RunReader, TaskCreationOptions.DenyChildAttach);
            lock (_stopLock) {
                if (!IsStarted)
                    throw new OperationCanceledException("The VPN adapter has been stopped while starting.");

                _readerThreadId = 0;
                _readerTask = readerTask;
                _readerRunId = readerTask.Id;
                readerTask.Start(TaskScheduler.Default);
            }

            VhLogger.Instance.LogInformation("TUN adapter started.");
        }
        catch (Exception ex) {
            // when a concurrent Stop() tears the adapter down mid-start, or the start is cancelled (a
            // terminal's Ctrl+C also kills the command it runs), the first step to fail is fallout of
            // that, so report the start as canceled rather than broken
            var isStopped = _isStopping || !IsStarted || cancellationToken.IsCancellationRequested;
            VhLogger.Instance.Log(ex is OperationCanceledException || isStopped ? LogLevel.Trace : LogLevel.Error,
                ex, "Failed to start TUN adapter.");
            Stop(false);

            if (isStopped && ex is not OperationCanceledException)
                throw new OperationCanceledException("The VPN adapter has been stopped while starting.", ex);
            throw;
        }
        finally {
            _isStarting = false;
        }
    }

    private async Task AddRouteHelper(IEnumerable<IpNetwork> ipNetworks, AddressFamily addressFamily,
        CancellationToken cancellationToken)
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
        foreach (var network in ipNetworks) {
            try {
                await AddRoute(network, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                throw new Exception($"Could not add {network} to route. {ex.Message}");
            }
        }
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private async Task SetAppFilters(IEnumerable<string>? includeApps, IEnumerable<string>? excludeApps, CancellationToken cancellationToken)
    {
        var appPackageId = AppPackageId;

        // validate the app filter
        if (appPackageId == null)
            throw new InvalidOperationException("AppPackageId must be available when AppFilter is supported.");

        if (!VhUtils.IsNullOrEmpty(includeApps) && !VhUtils.IsNullOrEmpty(excludeApps))
            throw new InvalidOperationException("Both include and exclude apps cannot be set at the same time.");

        // make sure current app is in the allowed list
        if (includeApps != null) {
            includeApps = includeApps.Concat([appPackageId]).Distinct();
            await SetAllowedApps(includeApps, cancellationToken);
        }

        // make sure current app is not in the disallowed list
        if (excludeApps != null) {
            excludeApps = excludeApps.Where(x => x != appPackageId).Distinct();
            await SetDisallowedApps(excludeApps, cancellationToken);
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // first: a restart under way starts nothing after its stop, and a pending retry ends
        _keepStarted = false;
        _keepStartedCts.TryCancel();
        using var restartLock = WaitForRestart();
        Stop(throwException: true);
    }

    // Returns whether this call stopped it: not when it was not started, or another call is stopping it.
    // The call that passes the guard owns the stop: the adapter counts as stopped whatever its teardown
    // did, as the next start removes a leftover, and a stop that went unnoticed would be the worse.
    private bool Stop(bool throwException)
    {
        lock (_stopLock) {
            if (!IsStarted || _isStopping)
                return false;

            VhLogger.Instance.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
            _isStopping = true;
            _readerRunId = 0; // this run's reader leaves, even one that outlives the stop's wait
            try {
                AdapterClose();
                AdapterRemove();
                VhLogger.Instance.LogInformation("TUN adapter stopped.");
            }
            catch (Exception ex) when (!throwException) {
                VhLogger.Instance.LogError(ex, "Failed to stop the TUN adapter. AdapterName: {AdapterName}",
                    AdapterName);
            }
            finally {
                PrimaryAdapterIpV4 = null;
                PrimaryAdapterIpV6 = null;
                AdapterIpNetworkV4 = null;
                AdapterIpNetworkV6 = null;
                GatewayIpV4 = null;
                GatewayIpV6 = null;
                IsStarted = false;
                _isStopping = false;
            }

            return true;
        }
    }

    protected virtual void BindSocketToIp(Socket socket, IPAddress address)
    {
        socket.Bind(new IPEndPoint(address, 0));
    }

    public virtual bool ProtectSocket(Socket socket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
        if (primaryAdapterIp == null)   
            return false;

        // bind the socket to the primary adapter IP
        BindSocketToIp(socket, primaryAdapterIp);
        return true;
    }

    public virtual bool ProtectSocket(Socket socket, IPAddress remoteAddress)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
        if (primaryAdapterIp == null) 
            return false;

        // could not protect loopback addresses or not needed at all, because loopback can not be routed
        if (IPAddress.IsLoopback(primaryAdapterIp) != IPAddress.IsLoopback(remoteAddress)) 
            return false;

        // bind the socket to the primary adapter IP and connect to the remote endpoint
        BindSocketToIp(socket, primaryAdapterIp);
        return true;
    }

    private void DiscoverPrimaryAdapterIps(bool protect)
    {
        var primaryAdapterIpV4 = DiscoverPrimaryAdapterIpViaProtect(AddressFamily.InterNetwork, protect);
        var primaryAdapterIpV6 = DiscoverPrimaryAdapterIpViaProtect(AddressFamily.InterNetworkV6, protect);

        // If the primary adapter IPs are changed, update them and notify subscribers
        if (!Equals(primaryAdapterIpV4, PrimaryAdapterIpV4) || !Equals(primaryAdapterIpV6, PrimaryAdapterIpV6)) {
            PrimaryAdapterIpV4 = primaryAdapterIpV4;
            PrimaryAdapterIpV6 = primaryAdapterIpV6;
            PrimaryAdapterIpChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IPAddress? DiscoverPrimaryAdapterIpViaProtect(AddressFamily addressFamily, bool protect)
    {
        using var udpClient = new UdpClient(addressFamily);
        if (protect) {
            try {
                var ipAddress = WebDeadNetworks.First(x => x.AddressFamily == addressFamily).Prefix;
                ProtectSocket(udpClient.Client, ipAddress);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to protect the socket for discovering primary adapter IP. AddressFamily: {AddressFamily}",
                    addressFamily);
                return null;
            }
        }
        return DiscoverPrimaryAdapterIp(udpClient);
    }

    private static IPAddress? DiscoverPrimaryAdapterIp(AddressFamily addressFamily)
    {
        using var udpClient = new UdpClient(addressFamily);
        return DiscoverPrimaryAdapterIp(udpClient);
    }

    private static IPAddress? DiscoverPrimaryAdapterIp(UdpClient protectedUdpClient)
    {
        var addressFamily = protectedUdpClient.Client.AddressFamily;
        // not matter is it reachable or not, just try to get the primary adapter IP which can route to the internet
        var ipAddress = WebDeadNetworks.First(x => x.AddressFamily == addressFamily).Prefix;
        var remoteEndPoint = new IPEndPoint(ipAddress, 53);

        try {
            // IPv6 needs the addressFamily to be set
            protectedUdpClient.Connect(remoteEndPoint);
            var localEndPoint = protectedUdpClient.Client.GetLocalEndPoint();

            // in IPV6, the local ip may be some random ip for another virtual adapter.
            // valid one should be global unicast and assigned to local interfaces
            if (addressFamily.IsV6() && !IpNetwork.AllGlobalUnicastV6.Contains(localEndPoint.Address))
                throw new Exception("The discovered primary adapter IP is not assigned to any local interface.");

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

    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
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
        catch (Exception ex) {
            _ioErrorCount++;
            if (_ioErrorCount < MaxIoErrorCount)
                throw;

            // too many errors in a row: the adapter is broken, so stop it, and recover or tell the owner
            if (StopBySender())
                OnUnrequestedStop(ex);
            throw;
        }
    }

    // the stop runs inside the sender's batch, so it must not wait for that batch; the previous mark is
    // restored, as this stop may run inside another adapter's. The sender's errors are the current run's.
    private bool StopBySender()
    {
        var previous = _senderErrorStopAdapter;
        _senderErrorStopAdapter = this;
        try {
            return StopOnIoErrors(_readerRunId);
        }
        finally {
            _senderErrorStopAdapter = previous;
        }
    }

    // The reader's or the sender's stop on its own I/O errors never waits for the stop lock: a stop that
    // holds it may be waiting for that very thread, and the teardown is that stop's anyway. It stops only
    // the run the errors came from, checked under the lock: a stop and a restart may have replaced it.
    private bool StopOnIoErrors(int runId)
    {
        if (!_stopLock.TryEnter())
            return false;

        try {
            return _readerRunId == runId && Stop(false);
        }
        finally {
            _stopLock.Exit();
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

            // a stop: the packet goes with the run, and no wait here holds the stop
            if (!IsReady)
                return;

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
            VhLogger.Instance.LogWarning("Failed to send packet via TUN adapter. AdapterName: {AdapterName}",
                AdapterName);
    }

    // The reader's thread is known, so a stop made on it does not wait for itself. Never faults: a stop
    // waits for the task.
    private void RunReader()
    {
        _readerThreadId = Environment.CurrentManagedThreadId;
        try {
            StartReadingPackets();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The TUN adapter's reader ended by an error.");
        }
    }

    /// <summary>
    /// Waits for this run's reader to end, for an adapter about to free what the reader uses. Returns
    /// false when it did not end in time. A stop made on the reader's own thread, by its read errors or
    /// by a subscriber, does not wait for itself.
    /// </summary>
    protected bool WaitForReader(TimeSpan timeout)
    {
        return Environment.CurrentManagedThreadId == _readerThreadId || _readerTask.Wait(timeout);
    }

    /// <summary>
    /// Waits for the batch being sent to end, for an adapter that has cleared the handle its WritePacket
    /// uses: that batch may still hold the handle, a later one finds it cleared. The sender writes its
    /// flag with no fence, so a process-wide barrier before the wait makes a flag set before the clearing
    /// seen here, and one after it makes all the batch did before clearing its flag seen too (ARM64 may
    /// show the clearing first). Returns false when it did not end in time. A stop made by this adapter's
    /// sender, on its own errors, does not wait for itself.
    /// </summary>
    protected bool WaitForSender(TimeSpan timeout)
    {
        if (_senderErrorStopAdapter == this)
            return true;

        Interlocked.MemoryBarrierProcessWide();
        var stopwatch = Stopwatch.StartNew();
        var spinWait = new SpinWait();
        while (IsSendingBatch) {
            if (stopwatch.Elapsed > timeout)
                return false;

            // never a sleep, a whole timer tick: under traffic, a check may meet one batch after another
            spinWait.SpinOnce(sleep1Threshold: -1);
        }

        Interlocked.MemoryBarrierProcessWide();
        return true;
    }

    protected virtual void StartReadingPackets()
    {
        // Read packets from TUN adapter while this reader's run is the current one: a stop retires it, so
        // a reader held in a subscriber past its stop's wait leaves instead of joining the next run
        var runId = Task.CurrentId ?? -1;
        while (_readerRunId == runId) {
            try {
                // read next packet; no packet: wait before retrying
                var packet = ReadPacket(_mtu);
                if (packet == null)
                    WaitForTunRead();

                // reset error counters if not exception, the wait's included
                _ioErrorCount = 0;
                if (packet == null)
                    continue;

                // process the packet
                OnPacketReceived(packet);
            }
            catch (Exception) when (_readerRunId != runId) {
                break; // normal stop
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in reading packets from TUN adapter.");
                _ioErrorCount++;
                if (_ioErrorCount < MaxIoErrorCount)
                    continue;

                // Too many errors in a row: the adapter is broken, so stop it, and recover or tell the
                // owner. Only here, not after the loop: a reader that ends with its run's stop may end
                // late, after a restart has started another run, which it must leave be. The loop's
                // check ends it: this stop, or another one under way, has retired its run.
                if (StopOnIoErrors(runId))
                    OnUnrequestedStop(ex);
            }
        }

        VhLogger.Instance.LogDebug("Finish reading the packets from the TUN adapter.");
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

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        _ = CheckAdapterInterface();
        CheckPrimaryAdapterAddresses();
    }

    // a link set down on Linux keeps its address, so only this event tells of it
    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _ = CheckAdapterInterface();
    }

    // The OS says its network moved: is our own interface still there and up? A disabled adapter gives
    // no I/O error, its reads just stop, so these events are the one signal of it. Under the restart
    // lock, so no restart runs between the look and the stop; one under way looks for itself. Only from
    // the OS events: a fresh interface may not report up yet at a start's end. Never throws.
    private async Task CheckAdapterInterface()
    {
        if (!RestartAfterNetworkAddressChanged || !IsStarted || !_keepStarted || _isStarting ||
            IsDisposing || IsDisposed)
            return;

        try {
            using var lockScope = await _restartLock.LockAsync(TimeSpan.Zero, CancellationToken.None).Vhc();
            if (!lockScope.Succeeded || !IsStarted || !_keepStarted || _isStarting || IsDisposing || IsDisposed)
                return;

            if (IsAdapterInterfaceUp())
                return;

            VhLogger.Instance.LogWarning("The VPN adapter's interface is gone or down.");
            if (Stop(false))
                OnUnrequestedStop(new InvalidOperationException("The VPN adapter's interface is gone or down."));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the VPN adapter's interface.");
        }
    }

    // Our interface, by our name or our address, present and not down. A tun may report Unknown while up.
    private bool IsAdapterInterfaceUp()
    {
        var adapterAddresses = new[] { AdapterIpNetworkV4?.Prefix, AdapterIpNetworkV6?.Prefix }.OfType<IPAddress>().ToArray();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase) ||
                         ni.GetIPProperties().UnicastAddresses.Any(a => adapterAddresses.Contains(a.Address)))
            .Any(ni => ni.OperationalStatus is not (OperationalStatus.Down or OperationalStatus.LowerLayerDown or OperationalStatus.NotPresent));
    }

    // Restarts, or rediscovers the primary IPs, where the host's addresses moved since the last look.
    // Never throws: the OS raises it on a thread of its own, and a start that succeeded must not fail by it.
    private void CheckPrimaryAdapterAddresses()
    {
        if (_isStarting || _isStopping || _restartLock.IsLocked || IsDisposing || IsDisposed)
            return;

        try {
            // check if primary adapter addresses actually changed
            var currentAddresses = GetPrimaryAdapterAddresses();
            if (currentAddresses.SetEquals(_lastPrimaryAdapterAddresses))
                return;
            _lastPrimaryAdapterAddresses = currentAddresses;

            // do not restart adapter if there is no primary network adapter up
            if (currentAddresses.Count == 0)
                return;

            VhLogger.Instance.LogInformation("Network address changed.");
            if (!RestartAfterNetworkAddressChanged) {
                DiscoverPrimaryAdapterIps(true);
                return;
            }

            // a restart is for a started adapter: a stopped one waits for its retry, or for its owner.
            // On the pool: the lock is free, so the restart's stop would run here, inside a start's end
            if (IsStarted && _keepStarted) {
                var keepStartedToken = _keepStartedCts.Token; // this run's, not a later one's
                _ = Task.Run(() => RunRestart(keepStartedToken));
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the primary adapter addresses.");
        }
    }

    private static bool IsPrimaryAdapter(NetworkInterface networkInterface)
    {
        return networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
               !networkInterface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
    }

    // The host's addresses, less our own adapter's: Windows and Linux name it after us, but Android's
    // tun0 and iOS's utunN are known by their address alone. By the address, not the network: a client's
    // virtual network is the server's whole pool, which a LAN may share.
    private HashSet<IPAddress> GetPrimaryAdapterAddresses()
    {
        var adapterAddresses = new[] { AdapterIpNetworkV4?.Prefix, AdapterIpNetworkV6?.Prefix }.OfType<IPAddress>().ToArray();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsPrimaryAdapter)
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .Where(ni => !ni.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase))
            .Select(ni => ni.GetIPProperties().UnicastAddresses.Select(a => a.Address).ToArray())
            .Where(addresses => !addresses.Any(adapterAddresses.Contains))
            .SelectMany(addresses => addresses)
            .ToHashSet();
    }

    private readonly AsyncLock _restartLock = new();

    /// <summary>
    /// Stops and starts the adapter again with its start options. A failed start leaves it stopped,
    /// and the adapter answers that itself: it retries with AutoRestart, else raises Failed.
    /// </summary>
    public async Task Restart(CancellationToken cancellationToken)
    {
        var startOptions = _startOptions ?? throw new InvalidOperationException("The adapter has not been started.");

        // the owner's stop ends this restart too, whatever token the caller passed; only that stop
        // makes a stopped adapter nobody's concern
        var keepStartedToken = _keepStartedCts.Token;
        if (!_keepStarted || keepStartedToken.IsCancellationRequested)
            return; // the owner stopped it: nothing to restart

        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, keepStartedToken);
        await RestartUnderLock(startOptions, keepStartedToken, cancellationTokenSource.Token).Vhc();

        // an address change during the restart was left to it: see it now, outside the lock
        CheckPrimaryAdapterAddresses();
    }

    private async Task RestartUnderLock(VpnAdapterOptions startOptions, CancellationToken keepStartedToken,
        CancellationToken cancellationToken)
    {
        using var lockScope = await _restartLock.LockAsync(TimeSpan.Zero, cancellationToken).Vhc();
        if (!lockScope.Succeeded)
            return; // already in progress

        // A stop or a disposal clears the keep flag, waits for this lock and then stops the adapter, so a
        // restart that sees the stop begun starts nothing; one already starting runs to its end, for that stop.
        if (!_keepStarted || IsDisposing || IsDisposed)
            return;

        VhLogger.Instance.LogInformation("Restarting VPN Adapter");
        try {
            // stop the adapter first, make sure ip discovery use correct routes
            Stop(throwException: true);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).Vhc();
            if (!_keepStarted || IsDisposing || IsDisposed)
                return;

            // rediscover the primary adapter IPs, in case they are changed during the stop-start process.
            // adapter is off so should not protect
            DiscoverPrimaryAdapterIps(protect: false);

            // start the adapter with the same options
            await StartCore(startOptions, cancellationToken).Vhc();
        }
        // this restart's own failure, not the owner's stop ending it: the adapter answers it
        catch (Exception ex) when (!keepStartedToken.IsCancellationRequested) {
            OnUnrequestedStop(ex);
            throw;
        }
    }

    // The adapter stopped on its own: by I/O errors, or by a restart whose start failed. With AutoRestart
    // it starts itself again after the delay, as long as its owner keeps it started; else the owner hears
    // Failed and must act, as nothing else starts it again.
    private void OnUnrequestedStop(Exception ex)
    {
        // still up: a caller's own cancellation ended its restart before the stop
        if (IsStarted || !_keepStarted || IsDisposing || IsDisposed)
            return;

        if (_autoRestart) {
            VhLogger.Instance.LogWarning(ex, "The VPN adapter is down. Restarting it in {Delay} seconds...",
                AutoRestartDelay.TotalSeconds);
            _ = RetryRestart(_keepStartedCts.Token);
            return;
        }

        // on a task of its own: a subscriber ends its session and disposes this adapter, which must not
        // run on the adapter's I/O thread, nor under the restart lock
        VhLogger.Instance.LogError(ex, "The VPN adapter is down and stays down.");
        var failed = Failed; // this run's subscribers, not a later one's
        Task.Run(() => VhUtils.TryInvoke("Failed", () => failed?.Invoke(this, ex)));
    }

    // A restart on a task of its own. Its failure is logged and answered by Restart.
    private async Task RunRestart(CancellationToken cancellationToken)
    {
        try {
            await Restart(cancellationToken).Vhc();
        }
        catch (Exception) {
            // logged and answered by Restart
        }
    }

    // The retry of a failed restart, unless something started the adapter meanwhile
    private async Task RetryRestart(CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(AutoRestartDelay, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return; // the owner's stop
        }

        if (!IsStarted)
            await RunRestart(cancellationToken).Vhc();
    }

    // A restart runs on a task of its own and would go on making what a stop removes, so a stop
    // waits for it. The lock is taken on the thread pool: no caller's synchronization context needed.
    private AsyncLock.ILockAsyncResult WaitForRestart()
    {
        return Task.Run(() => _restartLock.LockAsync(CancellationToken.None)).GetAwaiter().GetResult();
    }

    protected sealed override void PreDispose()
    {
        _keepStarted = false;
        _keepStartedCts.TryCancel();
        using var restartLock = WaitForRestart();
        Stop(false);
        base.PreDispose();
    }

    protected override void DisposeManaged()
    {
        // notify the subscribers that the adapter is disposed
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        Disposed?.Invoke(this, EventArgs.Empty);
        Disposed = null;

        base.DisposeManaged();
    }
}