using System.Net.NetworkInformation;
using PacketDotNet;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public abstract class PingProxy : IDisposable
{
    private readonly IPacketProxyReceiver _packetProxyReceiver;
    protected Ping Ping;
    public TimeSpan IcmpTimeout { get; set; }

    protected PingProxy(IPacketProxyReceiver packetProxyReceiver, TimeSpan? icmpTimeout)
    {
        _packetProxyReceiver = packetProxyReceiver;
        IcmpTimeout = icmpTimeout ?? TimeSpan.FromSeconds(30);
        Ping = new Ping();
        Ping.PingCompleted += PingCompleted;
    }

    private void PingCompleted(object sender, PingCompletedEventArgs e)
    {
        var ipPacket = (IPPacket)e.UserState;
        var pingReply = e.Reply;
        var icmpPacket = BuildIcmpReplyPacket(ipPacket, pingReply);

        ipPacket.ParentPacket = icmpPacket;
        ipPacket.DestinationAddress = ipPacket.SourceAddress;
        ipPacket.SourceAddress = pingReply.Address;
        PacketUtil.UpdateIpPacket(ipPacket);
        _packetProxyReceiver.OnPacketReceived(ipPacket);
    }

    public abstract Packet BuildIcmpReplyPacket(IPPacket ipPacket, PingReply pingReply);
    public abstract void Send(IPPacket ipPacket);
    public void Cancel() => Ping.SendAsyncCancel();

    public virtual void Dispose()
    {
        Ping.Dispose();
    }
}