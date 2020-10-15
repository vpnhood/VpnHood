using PacketDotNet;
using System;

namespace VpnHood.Client
{
    public interface IDeviceInbound : IDevice
    {
        void ProtectSocket(System.Net.Sockets.Socket socket);
        void SendPacketToInbound(IPPacket packet);
        event EventHandler<DevicePacketArrivalEventArgs> OnPacketArrivalFromInbound;
    }
}
