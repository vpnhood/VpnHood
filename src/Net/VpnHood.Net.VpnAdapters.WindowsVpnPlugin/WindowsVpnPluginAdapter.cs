using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Networking.Vpn;

namespace VpnHood.Net.VpnAdapters.WindowsVpnPlugin;

/// <summary>
/// Bridges VpnHood's packet transport to a Windows.Networking.Vpn channel. Windows supplies
/// outbound packets through Encapsulate; inbound VpnHood packets are injected through the direct
/// VpnChannel receive-buffer APIs available on the minimum supported Windows version.
/// </summary>
public sealed class WindowsVpnPluginAdapter(VpnChannel vpnChannel, VpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    private const int PollTimeoutMs = 1000;

    // The MTU and the frame size verified with packets end to end; another pair needs a packet-level test of its own.
    private const int WindowsMaxMtu = 1400;
    private const int WindowsMaxFrameSize = 1500;

    private readonly List<HostName> _clientIpV4List = [];
    private readonly List<HostName> _clientIpV6List = [];
    private readonly List<VpnRoute> _routesV4 = [];
    private readonly List<VpnRoute> _routesV6 = [];
    private readonly List<HostName> _dnsServers = [];
    private int _mtu = WindowsMaxMtu;

    private readonly ConcurrentQueue<(byte[] Data, int Length)> _readQueue = new();

    // Never disposed: Windows may still be returning from an Encapsulate callback after the adapter is
    // disposed. The closed generation gate keeps that callback from enqueueing, and the semaphore is
    // collected with the adapter once Windows releases the callback target.
    private readonly SemaphoreSlim _readSignal = new(0);
    private readonly Lock _queueLock = new();
    private readonly int _readQueueCapacity = Math.Max(256, adapterSettings.QueueCapacity ?? 0);
    private int _readQueueCount;
    private long _queueGeneration;
    private int _queueFullWarningLogged;
    private int _outboundSizeWarningLogged;
    private int _inboundSizeWarningLogged;

    private DatagramSocket? _transport;
    private readonly byte[] _writeBuffer = new byte[ushort.MaxValue];
    private readonly Lock _stateLock = new();
    private bool _channelStarted;
    private bool _osStopping;
    private volatile bool _closed = true;
    private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;
    public override bool IsAppFilterSupported => false;
    public override bool IsNatSupported(IpVersion ipVersion) => false;

    // the VPN host's own sockets leave by the physical adapter, bound to its address
    protected override bool IsSocketProtectedByBind => true;
    protected override string? AppPackageId => null;

    // Windows owns the interface, its addresses and their changes
    protected override bool RestartAfterNetworkAddressChanged => false;
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    // Windows is stopping the channel itself, so the close must not stop it again.
    public void NotifyOsStopping()
    {
        lock (_stateLock)
            _osStopping = true;
    }

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        _clientIpV4List.Clear();
        _clientIpV6List.Clear();
        _routesV4.Clear();
        _routesV6.Clear();
        _dnsServers.Clear();
        _mtu = WindowsMaxMtu;
        lock (_stateLock)
            _osStopping = false;

        ResetPacketQueue(open: false);
        return Task.CompletedTask;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        (ipNetwork.IsV4 ? _clientIpV4List : _clientIpV6List)
            .Add(new HostName(ipNetwork.Prefix.ToString()));
        return Task.CompletedTask;
    }

    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        (ipNetwork.IsV4 ? _routesV4 : _routesV6)
            .Add(new VpnRoute(new HostName(ipNetwork.Prefix.ToString()), (byte)ipNetwork.PrefixLength));
        return Task.CompletedTask;
    }

    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        _dnsServers.Clear();
        _dnsServers.AddRange(dnsServers.Select(x => new HostName(x.ToString())));
        return Task.CompletedTask;
    }

    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _mtu = Math.Min(mtu, WindowsMaxMtu);
        return Task.CompletedTask;
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        vpnChannel.LogDiagnosticMessage($"Session: {sessionName}");
        return Task.CompletedTask;
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken) =>
        throw new NotSupportedException("NAT is not supported by the Windows VPN plug-in adapter.");

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Application filtering is not implemented yet.");

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Application filtering is not implemented yet.");

    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Starting the Windows VPN plug-in data path...");

        // Recreated here, not in AdapterAdd, because a restart may run AdapterClose and AdapterOpen
        // without AdapterAdd and would otherwise keep a canceled source.
        if (_started.Task.IsCompleted)
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverIp = ServerIp ?? throw new InvalidOperationException("The VPN server IP is not available.");

        var transport = new DatagramSocket();
        _transport = transport;

        // Association must be the first VPN operation on this socket and must happen before connect.
        // This socket carries no VpnHood data; direct receive-buffer injection handles the data path.
        // Connect it to the remote VPN server address so the package has no AppContainer loopback
        // dependency. UDP connect performs no handshake and port 9 never receives application data.
        vpnChannel.AssociateTransport(transport, null);
        await transport.BindServiceNameAsync("0").AsTask(cancellationToken).Vhc();
        await transport.ConnectAsync(new HostName(serverIp.ToString()), "9")
            .AsTask(cancellationToken).Vhc();

        var routeAssignment = new VpnRouteAssignment();
        // Never replace these WinRT-owned vectors with managed Lists: Windows returns the misleading
        // E_OUTOFMEMORY (0x8007000E) from StartWithMainTransport when that ABI boundary is crossed.
        foreach (var route in _routesV4)
            routeAssignment.Ipv4InclusionRoutes.Add(route);
        foreach (var route in _routesV6)
            routeAssignment.Ipv6InclusionRoutes.Add(route);

        VpnDomainNameAssignment? dnsAssignment = null;
        if (_dnsServers.Count > 0) {
            dnsAssignment = new VpnDomainNameAssignment();
            dnsAssignment.DomainNameList.Add(new VpnDomainNameInfo(
                ".", VpnDomainNameType.Suffix, _dnsServers, Array.Empty<HostName>()));
        }

        ResetPacketQueue(open: true);
        vpnChannel.StartWithMainTransport(
            _clientIpV4List.Count == 0 ? null : _clientIpV4List,
            _clientIpV6List.Count == 0 ? null : _clientIpV6List,
            null,
            routeAssignment,
            dnsAssignment,
            (uint)_mtu,
            WindowsMaxFrameSize,
            false,
            transport);

        lock (_stateLock)
            _channelStarted = true;
        _started.TrySetResult();
        VhLogger.Instance.LogInformation("Windows VPN channel started. MTU: {Mtu}", _mtu);
    }

    protected override void AdapterClose()
    {
        ResetPacketQueue(open: false);
        _started.TrySetCanceled();

        var transport = Interlocked.Exchange(ref _transport, null);

        bool stopChannel;
        lock (_stateLock) {
            stopChannel = _channelStarted && !_osStopping;
            _channelStarted = false;
        }

        if (stopChannel) {
            try { vpnChannel.Stop(); }
            catch (Exception ex) { VhLogger.Instance.LogWarning(ex, "Could not stop the Windows VPN channel."); }
        }

        try { transport?.Dispose(); }
        catch (Exception ex) { VhLogger.Instance.LogDebug(ex, "Could not dispose the VPN transport."); }
    }

    // Windows owns the interface; the close has already stopped the channel.
    protected override void AdapterRemove()
    {
    }

    public void OnEncapsulate(VpnPacketBufferList packets, VpnPacketBufferList encapsulatedPackets)
    {
        // Windows keeps calling Encapsulate until the channel fully stops, which can be after this
        // adapter is closed or disposed. Leaving the buffers untouched is safe: Windows reclaims
        // buffers left in the source list after Encapsulate returns.
        long generation;
        lock (_queueLock) {
            if (_closed)
                return;
            generation = _queueGeneration;
        }

        // Do not remove the supplied buffers. Windows reclaims buffers left in the source list
        // after Encapsulate returns; only buffers appended to encapsulatedPackets are transmitted
        // through the associated outer transport, and a buffer taken out and kept drains the finite
        // pool: traffic works, then freezes. VpnHood consumes the managed copies below.
        // C#/WinRT's IEnumerable projection for VpnPacketBufferList can yield no elements even
        // when Size is nonzero. Rotate the exact initial count through the list instead; appending
        // each item back preserves ownership and order for Windows to reclaim on return.
        var packetCount = packets.Size;
        for (var i = 0u; i < packetCount; i++) {
            var packet = packets.RemoveAtBegin();
            packets.Append(packet);
            var length = (int)packet.Buffer.Length;
            if (length == 0)
                continue;

            var data = ArrayPool<byte>.Shared.Rent(length);
            try {
                packet.Buffer.CopyTo(0, data, 0, length);
            }
            catch {
                ArrayPool<byte>.Shared.Return(data);
                throw;
            }

            lock (_queueLock) {
                // Close/reopen can complete while the payload is being copied. A generation check
                // prevents that old callback from injecting packets into the next VPN session.
                if (_closed || generation != _queueGeneration) {
                    ArrayPool<byte>.Shared.Return(data);
                    return;
                }

                if (_readQueueCount >= _readQueueCapacity) {
                    ArrayPool<byte>.Shared.Return(data);
                    if (Interlocked.Exchange(ref _queueFullWarningLogged, 1) == 0)
                        VhLogger.Instance.LogWarning(
                            "Windows VPN packet queue is full; packets will be dropped until capacity is available.");
                    continue;
                }

                _readQueue.Enqueue((data, length));
                _readQueueCount++;
                _readSignal.Release();
            }
        }
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        while (true) {
            (byte[] Data, int Length) item;
            lock (_queueLock) {
                if (!_readQueue.TryDequeue(out item))
                    return false;
                _readQueueCount--;
                _ = _readSignal.Wait(0);
            }

            try {
                // A packet larger than the caller's buffer is a drop, not "queue empty"; keep
                // reading so the packets behind it are not delayed by a poll timeout.
                if (item.Length > buffer.Length) {
                    if (Interlocked.Exchange(ref _outboundSizeWarningLogged, 1) == 0)
                        VhLogger.Instance.LogWarning(
                            "Windows VPN dropped an outbound packet larger than the read buffer. Packet: {PacketLength}, Buffer: {BufferLength}",
                            item.Length, buffer.Length);
                    continue;
                }

                Buffer.BlockCopy(item.Data, 0, buffer, 0, item.Length);
                return true;
            }
            finally {
                ArrayPool<byte>.Shared.Return(item.Data);
            }
        }
    }

    protected override void WaitForTunRead() => _readSignal.Wait(PollTimeoutMs);

    protected override bool WritePacket(IpPacket ipPacket)
    {
        if (_closed || _transport is null)
            throw new InvalidOperationException("Adapter is not open.");
        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);

        // StartWithMainTransport sizes its receive-buffer pool from the MTU argument. maxFrameSize
        // applies to the send pool, so checking only 1500 can overrun a 1280/1400-byte buffer.
        if (length > _mtu) {
            if (Interlocked.Exchange(ref _inboundSizeWarningLogged, 1) == 0)
                VhLogger.Instance.LogWarning(
                    "Windows VPN dropped an inbound packet larger than the channel MTU. Packet: {PacketLength}, MTU: {Mtu}",
                    length, _mtu);
            return true;
        }

        var packet = vpnChannel.GetVpnReceivePacketBuffer();
        var appended = false;
        try {
            buffer.CopyTo(offset, packet.Buffer, 0, length);
            packet.Buffer.Length = (uint)length;
            vpnChannel.AppendVpnReceivePacketBuffer(packet);
            appended = true;
            vpnChannel.FlushVpnReceivePacketBuffers();
        }
        catch {
            // Every buffer obtained from the VPN pool must be returned to the platform.
            if (!appended) {
                try {
                    packet.Buffer.Length = 0;
                    vpnChannel.AppendVpnReceivePacketBuffer(packet);
                    vpnChannel.FlushVpnReceivePacketBuffers();
                }
                catch { /* The channel is already stopping; Windows will reclaim its pool. */ }
            }
            throw;
        }

        return true;
    }

    protected override void WaitForTunWrite() => Thread.Sleep(1);

    private void ResetPacketQueue(bool open)
    {
        lock (_queueLock) {
            _closed = true;
            _queueGeneration++;
            while (_readQueue.TryDequeue(out var item))
                ArrayPool<byte>.Shared.Return(item.Data);
            _readQueueCount = 0;
            while (_readSignal.Wait(0)) { }

            // Wake a reader blocked during an in-place adapter restart. A newly opened session
            // drains this permit; a stopping read loop consumes it and observes IsReady=false.
            if (!open)
                _readSignal.Release();
            else {
                while (_readSignal.Wait(0)) { }
                Interlocked.Exchange(ref _queueFullWarningLogged, 0);
                Interlocked.Exchange(ref _outboundSizeWarningLogged, 0);
                Interlocked.Exchange(ref _inboundSizeWarningLogged, 0);
                _closed = false;
            }
        }
    }
}
