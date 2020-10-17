using PacketDotNet;
using System;

namespace VpnHood.Client
{
    public class DevicePacketArrivalEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }
        public IPacketCapture Device { get; }
        public bool IsHandled { get; set; }

        public DevicePacketArrivalEventArgs(IPPacket ipPacket, IPacketCapture device)
        {
            IpPacket = ipPacket;
            Device = device;
        }
    }
}