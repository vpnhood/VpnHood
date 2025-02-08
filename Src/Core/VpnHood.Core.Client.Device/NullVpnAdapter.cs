using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Client.Device;

public class NullVpnAdapter : IVpnAdapter
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public virtual bool Started { get; set; }
    public virtual bool IsDnsServersSupported { get; set; } = true;
    public virtual bool IsMtuSupported { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;
    public virtual bool CanSendPacketToOutbound { get; set; }
    public bool CanDetectInProcessPacket { get; set; } = true;

    public virtual void StartCapture(VpnAdapterOptions options)
    {
        Started = true;
        _ = PacketReceivedFromInbound; //prevent not used warning
    }

    public virtual void StopCapture()
    {
        Started = false;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public virtual void ProtectSocket(Socket socket)
    {
        // nothing
    }

    public virtual void SendPacketToInbound(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPacketToInbound(IList<IPPacket> packets)
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

    public bool IsInProcessPacket(ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        if (!CanDetectInProcessPacket)
            throw new NotSupportedException("This device can not detect IsInProcessPacket.");

        return false;
    }

    public virtual void Dispose()
    {
        StopCapture();
    }
}