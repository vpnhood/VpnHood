using System;
using System.Threading.Tasks;
using PacketDotNet;

namespace VpnHood.Tunneling;

public interface IPacketProxyPool : IDisposable
{
    public Task SendPacket(IPPacket ipPacket);
    public int LocalEndPointCount { get; }
    public int RemoteEndPointCount { get; }
    public int MaxLocalEndPointCount { get; set; }
}