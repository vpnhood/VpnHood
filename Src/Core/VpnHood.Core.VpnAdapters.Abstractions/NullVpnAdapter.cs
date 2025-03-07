using System.Net.Sockets;
using PacketDotNet;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class NullVpnAdapter : IVpnAdapter
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public event EventHandler? Disposed;
    public virtual bool Started { get; set; }
    public virtual bool IsDnsServerSupported { get; set; } = true;
    public virtual bool IsNatSupported { get; set; } = true;
    public virtual bool CanProtectClient { get; set; } = true;

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

    public virtual void ProtectSocket(Socket socket)
    {
        // nothing
    }

    public TcpClient CreateProtectedTcpClient(AddressFamily addressFamily)
    {
        return new TcpClient(addressFamily);
    }

    public UdpClient CreateProtectedUdpClient(AddressFamily addressFamily)
    {
        return new UdpClient(addressFamily);
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

    public virtual void SendPacket(IPPacket ipPacket)
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