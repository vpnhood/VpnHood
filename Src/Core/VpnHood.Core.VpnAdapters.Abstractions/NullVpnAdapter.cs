using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class NullVpnAdapter(bool autoDisposePackets, bool blocking) :
    TunVpnAdapter(new VpnAdapterSettings {
        AdapterName = "NullAdapter",
        Blocking = blocking,
        AutoDisposePackets = autoDisposePackets
    })
{
    public override bool IsAppFilterSupported => true;
    public override bool IsNatSupported => true;
    protected override bool IsSocketProtectedByBind  =>false;
    protected override string AppPackageId => "VpnHood.NullAdapter";
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task SetAllowedApps(string[] packageIds, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task SetDisallowedApps(string[] packageIds, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override void AdapterRemove()
    {
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
    }

    protected override void WaitForTunWrite()
    {
    }

    private ManualResetEventSlim? _readBlockEvent = new(false);

    protected override void WaitForTunRead()
    {
        // wait for dispose or unblock
        // NullVpnAdapter never returns any packets, so we need to wait for unblock
        _readBlockEvent?.Wait();
        _readBlockEvent?.Dispose();
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        return false; // there is no packet
    }

    protected override bool WritePacket(IpPacket ipPacket)
    {
        return true;
    }

    protected override void DisposeManaged()
    {
        _readBlockEvent?.Set();
        _readBlockEvent = null;
        base.DisposeManaged();
    }
}