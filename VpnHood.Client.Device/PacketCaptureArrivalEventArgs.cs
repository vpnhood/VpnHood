using PacketDotNet;
using System;

namespace VpnHood.Client.Device
{
    public class PacketCaptureArrivalEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }
        public IPacketCapture PacketCapture { get; }
        public bool IsHandled { get; set; }

        public PacketCaptureArrivalEventArgs(IPPacket ipPacket, IPacketCapture packetCapture)
        {
            IpPacket = ipPacket;
            PacketCapture = packetCapture;
        }
    }
}