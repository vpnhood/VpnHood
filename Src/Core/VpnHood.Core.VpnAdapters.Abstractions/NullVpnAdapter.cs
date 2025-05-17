using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class NullVpnAdapter(bool autoDisposePackets, bool blocking) :
    PacketTransport(new PacketTransportOptions {
        Blocking = blocking,
        AutoDisposePackets = autoDisposePackets
    }),
    IVpnAdapter
{
    public event EventHandler? Disposed;
    public virtual bool IsStarted { get; set; }
    public virtual bool IsNatSupported { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;

    public virtual Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        IsStarted = true;
        return Task.CompletedTask;
    }

    public virtual void Stop()
    {
        IsStarted = false;
    }

    public virtual bool ProtectSocket(Socket socket)
    {
        return true;
    }

    public virtual bool ProtectSocket(Socket socket, IPAddress ipAddress)
    {
        return true;
    }

    protected override ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        // Just discard the packets
        return default;
    }

    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback;
    }

    public bool IsIpVersionSupported(IpVersion ipVersion)
    {
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Stop();
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        base.Dispose(disposing);
    }
}