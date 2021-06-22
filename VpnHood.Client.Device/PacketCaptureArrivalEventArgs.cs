using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VpnHood.Client.Device
{
    public class PacketCaptureArrivalEventArgs : EventArgs
    {
        public class ArivalPacket
        {
            public ArivalPacket(IPPacket ipPacket) => IpPacket = ipPacket;
            public IPPacket IpPacket { get; }
            public bool IsHandled { get; set; }
        }

        public IEnumerable<ArivalPacket> ArivalPackets { get; }
        public IPacketCapture PacketCapture { get; }

        public PacketCaptureArrivalEventArgs(IEnumerable<IPPacket> ipPackets, IPacketCapture packetCapture)
        {
            ArivalPackets = ipPackets.Select(x => new ArivalPacket(x));
            PacketCapture = packetCapture;
        }
    }
}