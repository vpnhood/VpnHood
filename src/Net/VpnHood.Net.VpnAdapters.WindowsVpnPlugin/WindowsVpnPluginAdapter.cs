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
/// Provides the VPN adapter of a Windows VPN plug-in, on the <see cref="VpnChannel"/> Windows gives the plug-in's
/// connect: Windows owns the interface, and the adapter starts the channel with the addresses, routes, DNS servers
/// and MTU a start gathers.
/// </summary>
/// <param name="vpnChannel">The channel Windows gave the plug-in's connect.</param>
/// <param name="adapterSettings">The adapter's settings.</param>
/// <remarks>
/// <para>
/// Windows hands the device's outgoing packets to the plug-in's <c>Encapsulate</c> callback, which passes them to
/// <see cref="OnEncapsulate"/>; the adapter queues copies for its reader, and writes incoming packets into the
/// channel's receive buffers. The channel's transport is a UDP socket connected to the VPN server's address, which
/// carries no data. The MTU is at most 1,400 bytes. There is no NAT and no app filter.
/// </para>
/// <para>
/// A close leaves the channel up: the plug-in stops it in Windows' <c>Disconnect</c>, as Windows takes a channel
/// stopped by itself for a dropped connection and dials again.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">
/// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
/// </exception>
public sealed class WindowsVpnPluginAdapter(VpnChannel vpnChannel, VpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    private const int PollTimeoutMs = 1000;
    private const int ReceivePoolEmptyHResult = unchecked((int)0x8007000E);
    private static readonly TimeSpan SenderStopWaitTimeout = TimeSpan.FromSeconds(1);

    // The MTU and the frame size verified with packets end to end; another pair needs a packet-level test of its own.
    private const int WindowsMaxMtu = 1400;
    private const int WindowsMaxFrameSize = 1500;

    private readonly List<HostName> _clientIpV4List = [];
    private readonly List<HostName> _clientIpV6List = [];
    private readonly List<IpNetwork> _routes = [];
    private readonly List<IPAddress> _dnsServers = [];
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
    private bool _receivePoolEmpty; // the write path's only: a warning per run of drops, not per packet

    private DatagramSocket? _transport;
    private readonly byte[] _writeBuffer = new byte[ushort.MaxValue];
    private volatile bool _closed = true;
    private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets a task that completes when the channel has started, at the end of a successful open, before the reader
    /// starts and <see cref="TunVpnAdapter.Started"/> is raised; it is canceled when the adapter closes or is disposed
    /// first.
    /// </summary>
    /// <remarks>
    /// An open that finds the task completed or canceled makes a new one as it begins, so read the property again once
    /// a later start has reached its open. A stop before any start does not cancel the task.
    /// </remarks>
    public Task StartedTask => _started.Task;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    public override bool IsAppFilterSupported => false;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="false"/>.</remarks>
    public override bool IsNatSupported(IpVersion ipVersion) => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>: the VPN host's own sockets leave by the physical adapter, bound to its address.</value>
    protected override bool IsSocketProtectedByBind => true;

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: there is no app filter.</value>
    protected override string? AppPackageId => null;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>: Windows owns the interface, its addresses and their changes.</value>
    protected override bool RestartAfterNetworkAddressChanged => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    /// <inheritdoc/>
    /// <remarks>
    /// It makes no interface, as Windows owns it: it forgets the settings an earlier start gathered, sets the MTU back
    /// to 1,400 bytes, and keeps the packet queue closed until the open.
    /// </remarks>
    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        _clientIpV4List.Clear();
        _clientIpV6List.Clear();
        _routes.Clear();
        _dnsServers.Clear();
        _mtu = WindowsMaxMtu;

        ResetPacketQueue(open: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the address, not its network's prefix length, for the open, which hands it to Windows.</remarks>
    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        (ipNetwork.IsV4 ? _clientIpV4List : _clientIpV6List)
            .Add(new HostName(ipNetwork.Prefix.ToString()));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the route for the open, which hands it to Windows.</remarks>
    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        _routes.Add(ipNetwork);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It keeps the servers for the open, which makes them the servers of every domain. Each server the routes include
    /// also gets a host route of its own in the channel, as Windows routes the physical adapter's DNS servers around
    /// the channel by host routes while it is connected.
    /// </remarks>
    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        _dnsServers.Clear();
        _dnsServers.AddRange(dnsServers);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It keeps the MTU for the open, at most 1,400 bytes, the size tested with packets end to end.</remarks>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _mtu = Math.Min(mtu, WindowsMaxMtu);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: Windows gives the channel's interface its metric, unless the VPN profile names one.</remarks>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>It writes the name to the channel's diagnostic log.</remarks>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        vpnChannel.LogDiagnosticMessage($"Session: {sessionName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the adapter has no NAT.</exception>
    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken) =>
        throw new NotSupportedException("NAT is not supported by the Windows VPN plug-in adapter.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the adapter has no app filter.</exception>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Application filtering is not implemented yet.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: the adapter has no app filter.</exception>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Application filtering is not implemented yet.");

    /// <inheritdoc/>
    /// <remarks>
    /// It associates a new UDP socket with the channel as its transport and connects it to the VPN server's address,
    /// then starts the channel with the gathered addresses, routes, DNS servers and MTU, opens the packet queue, and
    /// completes <see cref="StartedTask"/>. A packet Windows hands over during the channel's start is dropped.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The run's options name no VPN server address.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while the socket was bound or connected.
    /// </exception>
    protected override async Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Starting the Windows VPN plug-in data path...");

        // a start after a stop needs a source of its own: the last one has completed or been cancelled
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

        // Windows routes the physical adapter's own DNS servers around the channel while it is connected, by host
        // routes, which outrun any wider route of it: a resolver the channel carries, if one of them, was reached
        // around it, every query. Its own host route in the channel keeps it there, by the profile's interface metric,
        // below the physical adapter's (the device sets it); one the routes leave out stays out.
        var routes = _routes.ToList();
        routes.AddRange(_dnsServers
            .Where(x => routes.Any(route => route.Contains(x)))
            .Select(x => new IpNetwork(x, x.IsV4 ? 32 : 128))
            .Where(x => !routes.Contains(x)));

        var routeAssignment = new VpnRouteAssignment();
        // Never replace these WinRT-owned vectors with managed Lists: Windows returns the misleading
        // E_OUTOFMEMORY (0x8007000E) from StartWithMainTransport when that ABI boundary is crossed.
        foreach (var route in routes)
            (route.IsV4 ? routeAssignment.Ipv4InclusionRoutes : routeAssignment.Ipv6InclusionRoutes)
                .Add(new VpnRoute(new HostName(route.Prefix.ToString()), (byte)route.PrefixLength));

        VpnDomainNameAssignment? dnsAssignment = null;
        if (_dnsServers.Count > 0) {
            dnsAssignment = new VpnDomainNameAssignment();
            dnsAssignment.DomainNameList.Add(new VpnDomainNameInfo(
                ".", VpnDomainNameType.Suffix, _dnsServers.Select(x => new HostName(x.ToString())).ToList(),
                []));
        }

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

        // Opened once the channel is: a write before would meet a channel not started. An Encapsulate during the
        // start finds the gate closed and leaves its buffers to Windows, a drop.
        ResetPacketQueue(open: true);
        _started.TrySetResult();
        VhLogger.Instance.LogInformation("Windows VPN channel started. MTU: {Mtu}", _mtu);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes the packet queue, dropping the packets in it, waits up to a second for a batch being sent, which it
    /// logs when the batch is late, cancels <see cref="StartedTask"/> if it has not completed, and disposes the
    /// channel's transport socket. The channel stays up.
    /// </remarks>
    protected override void AdapterClose()
    {
        ResetPacketQueue(open: false);

        // The batch being sent leaves before this returns, as the plug-in stops the channel next and a write must not
        // be inside it then: the batch ends at its next packet, the adapter no longer ready. Bounded, as Windows'
        // Disconnect waits on it. The reader needs no wait: it touches only the managed queue.
        if (!WaitForSender(SenderStopWaitTimeout))
            VhLogger.Instance.LogWarning("The VPN adapter's sender did not leave in time; the channel may stop under it.");

        _started.TrySetCanceled();

        // The channel stays up: the plug-in stops it in Windows' Disconnect, as a channel it stops by itself is a
        // dropped connection, which Windows dials again.
        var transport = Interlocked.Exchange(ref _transport, null);
        try { transport?.Dispose(); }
        catch (Exception ex) { VhLogger.Instance.LogDebug(ex, "Could not dispose the VPN transport."); }
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: Windows owns the interface and the channel.</remarks>
    protected override void AdapterRemove()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It cancels <see cref="StartedTask"/> if it has not completed.</remarks>
    // An adapter disposed before it started never starts: a session the server refused, say. The close, which
    // ends a start otherwise, runs only for a started adapter.
    protected override void DisposeManaged()
    {
        _started.TrySetCanceled();
        base.DisposeManaged();
    }

    /// <summary>
    /// Takes the device's outgoing packets Windows hands to the plug-in's <c>Encapsulate</c> callback: it queues a copy
    /// of each for the adapter's reader, and leaves the buffers in <paramref name="packets"/>, where Windows takes them
    /// back.
    /// </summary>
    /// <param name="packets">
    /// The packets, whose buffers stay in the list, in their order when the call reads them all.
    /// </param>
    /// <param name="encapsulatedPackets">
    /// The list whose buffers Windows sends through the channel's transport; the adapter adds none.
    /// </param>
    /// <remarks>
    /// While the adapter is not open, the packets are left to Windows unread. After copying a packet, the call checks
    /// whether the adapter has closed or opened again since it began; if so, it discards the copy and leaves the rest
    /// unread. A packet is dropped when the queue is full: it holds
    /// <see cref="VpnHood.Net.PacketTransports.PacketTransportOptions.QueueCapacity"/> packets, or 256 if that is fewer
    /// or not set; the first such drop of an open is logged. An empty buffer is skipped. Windows may call it after a
    /// close or a disposal.
    /// </remarks>
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

    /// <inheritdoc/>
    /// <remarks>
    /// It takes the next packet from the queue <see cref="OnEncapsulate"/> fills. A packet longer than the buffer is
    /// dropped, which is logged once an open, and the next is read.
    /// </remarks>
    protected override int ReadPacket(byte[] buffer)
    {
        while (true) {
            (byte[] Data, int Length) item;
            lock (_queueLock) {
                if (!_readQueue.TryDequeue(out item))
                    return 0;
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
                return item.Length;
            }
            finally {
                ArrayPool<byte>.Shared.Return(item.Data);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>It waits up to a second for a packet to be queued; a close wakes it.</remarks>
    protected override void WaitForTunRead() => _readSignal.Wait(PollTimeoutMs);

    /// <inheritdoc/>
    /// <returns>
    /// <see langword="true"/> if the channel took the packet, or the packet was dropped: one longer than the MTU, which
    /// is logged once an open, or one Windows has no receive buffer for, which is logged once a run of such drops;
    /// <see langword="false"/> if the adapter is not open.
    /// </returns>
    /// <remarks>
    /// Windows' receive buffers run out when the VPN host falls behind, as while Windows freezes it between VPN events;
    /// a dropped packet is lost, unless its sender, as TCP's does, sends it again.
    /// </remarks>
    protected override bool WritePacket(IpPacket ipPacket)
    {
        // not open: no channel to take the packet, as a TUN's device before its start
        if (_closed)
            return false;

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

        // Windows' receive pool runs dry when the host falls behind it - frozen between VPN events while no app holds
        // it, Windows' rule for a plug-in - and says so as E_OUTOFMEMORY. A full ring drops the packet, as a TUN's
        // does, and TCP resends it; taken for the adapter's end, ten in a row ended the VPN.
        VpnPacketBuffer packet;
        try {
            packet = vpnChannel.GetVpnReceivePacketBuffer();
        }
        catch (Exception ex) when (ex.HResult == ReceivePoolEmptyHResult) {
            if (!_receivePoolEmpty) {
                _receivePoolEmpty = true;
                VhLogger.Instance.LogWarning(
                    "Windows VPN receive pool is empty; inbound packets are dropped until Windows returns its buffers.");
            }
            return true;
        }

        _receivePoolEmpty = false;
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

    /// <inheritdoc/>
    /// <remarks>It sleeps a millisecond.</remarks>
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
