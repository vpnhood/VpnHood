using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Core.Packets;

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


    public virtual void SendPacket(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPackets(IList<IPPacket> ipPackets)
    {
        // nothing
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