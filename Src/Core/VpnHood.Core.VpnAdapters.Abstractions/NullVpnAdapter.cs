using System.Net.Sockets;
using PacketDotNet;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class NullVpnAdapter : IVpnAdapter
{
    private bool _disposed;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;
    public virtual bool Started { get; set; }
    public virtual bool IsDnsServersSupported { get; set; } = true;
    public virtual bool IsNatSupported { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;
    public virtual bool CanSendPacketToOutbound { get; set; }

    public virtual Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        Started = true;
        _ = PacketReceivedFromInbound; //prevent not used warning
        return Task.CompletedTask;
    }

    public virtual void Stop()
    {
        Started = false;    
    }

    public virtual void ProtectSocket(Socket socket)
    {
        // nothing
    }

    public virtual void SendPacketToInbound(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPacketToInbound(IList<IPPacket> ipPackets)
    {
        // nothing
    }

    public virtual void SendPacketToOutbound(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        // nothing
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}