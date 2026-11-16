using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Represents a VPN adapter with no interface: it reads no packets and takes every packet written to it, for
/// tests and for running a VPN without a tunnel on the host.
/// </summary>
/// <remarks>
/// It needs no rights: its start and stop touch nothing on the host, it supports NAT and app filters, and it
/// neither restarts nor checks its interface when the host's network changes. A packet written to it is disposed:
/// nothing takes it on.
/// </remarks>
public class NullVpnAdapter : TunVpnAdapter
{
    // the wait of the run this thread reads for
    [ThreadStatic] private static ManualResetEventSlim? _threadReaderRelease;

    // The wait of the run that is up, or of the next one: a stop sets it and makes the next run's, so a reader
    // never waits on into another run. Never reset: a waiter may miss a set that a reset follows. It holds no
    // handle to dispose.
    private volatile ManualResetEventSlim _readerRelease = new(false);

    /// <summary>
    /// Initializes a new instance of the <see cref="NullVpnAdapter"/> class, named "NullAdapter".
    /// </summary>
    /// <param name="blocking">
    /// <see langword="true"/> to make a send wait for room when the queue is full; <see langword="false"/> to drop
    /// the oldest packet instead.
    /// </param>
    public NullVpnAdapter(bool blocking)
        : this(new VpnAdapterSettings {
            AdapterName = "NullAdapter",
            Blocking = blocking
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
    /// <remarks>It releases the run's reader, which then leaves.</remarks>
    protected override void AdapterClose()
    {
        var readerRelease = _readerRelease;
        _readerRelease = new ManualResetEventSlim(false);
        readerRelease.Set();
    }

    /// <inheritdoc/>
    protected override void WaitForTunWrite()
    {
    }

    /// <inheritdoc/>
    // The reader keeps the wait it found at its start: its own run's, which that run's stop sets. Read at each
    // wait instead, it could be the next run's, where the stop came between the reader's look at its run and its wait.
    protected override void StartReadingPackets()
    {
        _threadReaderRelease = _readerRelease;
        try {
            base.StartReadingPackets();
        }
        finally {
            _threadReaderRelease = null;
        }
    }

    /// <summary>
    /// Blocks the reader until its run is stopped, as no packet ever arrives.
    /// </summary>
    protected override void WaitForTunRead()
    {
        (_threadReaderRelease ?? _readerRelease).Wait();
    }

    /// <summary>
    /// Reads no packet.
    /// </summary>
    /// <param name="buffer">Not used.</param>
    /// <returns>Always 0.</returns>
    protected override int ReadPacket(byte[] buffer)
    {
        return 0; // there is no packet
    }

    /// <summary>
    /// Takes a packet and drops it; the adapter's transport disposes it, as it does every written packet.
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
        _readerRelease.Set();
        base.DisposeManaged();
    }
}
