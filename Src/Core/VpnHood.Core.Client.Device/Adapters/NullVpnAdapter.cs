using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Logging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Client.Device.Adapters;

public class NullVpnAdapter : IVpnAdapter
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;
    public virtual bool Started { get; set; }
    public virtual bool IsDnsServersSupported { get; set; } = true;
    public virtual bool IsMtuSupported { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;
    public virtual bool CanSendPacketToOutbound { get; set; }
    public bool CanDetectInProcessPacket { get; set; } = true;

    public NullVpnAdapter()
    {
        VhLogger.Instance.LogInformation("A NullVpnAdapter has been created.");
    }

    public virtual void StartCapture(VpnAdapterOptions options)
    {
        VhLogger.Instance.LogInformation("The null adapter has been started. No packet will go through the VPN.");

        Started = true;
        _ = PacketReceivedFromInbound; //prevent not used warning
    }

    public virtual void StopCapture()
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

    private bool _disposed;
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopCapture();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}