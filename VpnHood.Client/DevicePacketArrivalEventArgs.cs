using PacketDotNet;
using System;

namespace VpnHood.Client
{
    public class DevicePacketArrivalEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }
        public IDevice Device { get; }
        public bool IsHandled { get; set; }

        public DevicePacketArrivalEventArgs(IPPacket ipPacket, IDevice device)
        {
            IpPacket = ipPacket;
            Device = device;
        }
    }
}