using System;
using System.Collections.Generic;
using PacketDotNet;

namespace VpnHood.Client.Device
{
    public class PacketReceivedEventArgs : EventArgs
    {
        public PacketReceivedEventArgs(IEnumerable<IPPacket> ipPackets, IPacketCapture packetCapture)
        {
            IpPackets = ipPackets ?? throw new ArgumentNullException(nameof(ipPackets));
            PacketCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
        }

        public IEnumerable<IPPacket> IpPackets { get; }
        public IPacketCapture PacketCapture { get; }
    }
}