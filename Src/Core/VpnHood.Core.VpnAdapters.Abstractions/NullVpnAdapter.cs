using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Transports;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class NullVpnAdapter : IVpnAdapter
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler? Disposed;
    public virtual bool Started { get; set; }
    public virtual bool IsNatSupported { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;

    public virtual Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        Started = true;
        _ = PacketReceived; //prevent not used warning
        return Task.CompletedTask;
    }

    public virtual void Stop()
    {
        Started = false;    
    }

    public virtual bool ProtectSocket(Socket socket)
    {
        return true;
    }

    public virtual bool ProtectSocket(Socket socket, IPAddress ipAddress)
    {
        return true;
    }


    public virtual void SendPacket(IpPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPackets(IList<IpPacket> ipPackets)
    {
        // nothing
    }

    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
            return ipVersion == IpVersion.IPv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback;
    }

    public bool IsIpVersionSupported(IpVersion ipVersion)
    {
        return true;
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}