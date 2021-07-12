using PacketDotNet;
using System;
using System.Collections.Generic;

namespace VpnHood.Client.Device
{
    public class PacketReceivedEventArgs : EventArgs
    {
        public IEnumerable<IPPacket> IpPackets { get; }
        public IPacketCapture PacketCapture { get; }
        public PacketReceivedEventArgs(IEnumerable<IPPacket> ipPackets, IPacketCapture packetCapture)
        {
            IpPackets = ipPackets ?? throw new ArgumentNullException(nameof(ipPackets));
            PacketCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
        }
    }
}