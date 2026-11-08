using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Represents a VPN adapter with no interface: it reads no packets and takes every packet written to it, for
/// tests and for running a VPN without a tunnel on the host.
/// </summary>
/// <remarks>
/// It needs no rights: every step of its start and stop does nothing, it supports NAT and app filters, and it
/// neither restarts nor checks its interface when the host's network changes.
/// </remarks>
public class NullVpnAdapter : TunVpnAdapter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullVpnAdapter"/> class, named "NullAdapter".
    /// </summary>
    /// <param name="autoDisposePackets">
    /// <see langword="true"/> to dispose each packet once sent; <see langword="false"/> to leave that to its new
    /// owner.
    /// </param>
    /// <param name="blocking">
    /// <see langword="true"/> to make a send wait for room when the queue is full; <see langword="false"/> to drop
    /// the oldest packet instead.
    /// </param>
    public NullVpnAdapter(bool autoDisposePackets, bool blocking)
        : this(new VpnAdapterSettings {
            AdapterName = "NullAdapter",
            Blocking = blocking,
            AutoDisposePackets = autoDisposePackets
        })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NullVpnAdapter"/> class with settings of a derived class's
    /// own.
    /// </summary>
    /// <param name="adapterSettings">The adapter's settings.</param>
    protected NullVpnAdapter(VpnAdapterSettings adapterSettings)
        : base(adapterSettings)
    {
    }

    /// <inheritdoc/>
    protected override bool RestartAfterNetworkAddressChanged => false;

    /// <inheritdoc/>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => false;

    /// <inheritdoc/>
    public override bool IsAppFilterSupported => true;

    /// <inheritdoc/>
    public override bool IsNatSupported(IpVersion ipVersion) => true;

    /// <inheritdoc/>
    protected override bool IsSocketProtectedByBind => false;

    /// <inheritdoc/>
    protected override string AppPackageId => "VpnHood.NullAdapter";

    /// <inheritdoc/>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override void AdapterRemove()
    {
    }

    /// <inheritdoc/>
    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override void AdapterClose()
    {
    }

    /// <inheritdoc/>
    protected override void WaitForTunWrite()
    {
    }

    private ManualResetEventSlim? _readBlockEvent = new(false);

    /// <summary>
    /// Blocks the reader until the adapter is disposed, as no packet ever arrives.
    /// </summary>
    protected override void WaitForTunRead()
    {
        // wait for dispose or unblock
        // NullVpnAdapter never returns any packets, so we need to wait for unblock
        _readBlockEvent?.Wait();
        _readBlockEvent?.Dispose();
    }

    /// <summary>
    /// Reads no packet.
    /// </summary>
    /// <param name="buffer">Not used.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    protected override bool ReadPacket(byte[] buffer)
    {
        return false; // there is no packet
    }

    /// <summary>
    /// Takes a packet and drops it.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    protected override bool WritePacket(IpPacket ipPacket)
    {
        return true;
    }

    /// <summary>
    /// Releases the blocked reader, then disposes the adapter.
    /// </summary>
    protected override void DisposeManaged()
    {
        _readBlockEvent?.Set();
        _readBlockEvent = null;

        base.DisposeManaged();
    }
}
