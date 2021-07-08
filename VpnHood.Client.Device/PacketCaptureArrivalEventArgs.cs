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
            private bool _passthru;

            public ArivalPacket(IPPacket ipPacket, bool isPassthruSupported)
            {
                IpPacket = ipPacket;
                IsPassthruSupported = isPassthruSupported;
            }

            public IPPacket IpPacket { get; }
            public bool IsPassthruSupported { get; }
            public bool Passthru
            {
                get => _passthru; set
                {
                    if (!IsPassthruSupported) throw new NotSupportedException($"{nameof(Passthru)} is not supported by this PacketCapture!");
                    _passthru = value;
                }
            }
            public bool IsHandled { get; set; }
        }

        public IEnumerable<ArivalPacket> ArivalPackets { get; }
        public IPacketCapture PacketCapture { get; }

        public PacketCaptureArrivalEventArgs(IEnumerable<IPPacket> ipPackets, IPacketCapture packetCapture)
        {
            if (ipPackets is null) throw new ArgumentNullException(nameof(ipPackets));
            ArivalPackets = ipPackets.Select(x => new ArivalPacket(x, packetCapture.IsPassthruSupported));
            PacketCapture = packetCapture;
        }
    }
}