using PacketDotNet;
using System;
using System.Collections.Generic;

namespace VpnHood.Client.Device
{
    public class PacketCaptureArrivalEventArgs : EventArgs
    {
        public IEnumerable<IPPacket> IpPackets { get; }
        public IPacketCapture PacketCapture { get; }
        public PacketCaptureArrivalEventArgs(IEnumerable<IPPacket> ipPackets, IPacketCapture packetCapture)
        {
            IpPackets = ipPackets ?? throw new ArgumentNullException(nameof(ipPackets));
            PacketCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
        }
    }
}