using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Net;

namespace VpnHood.Client.Device
{
    public interface IPacketCapture : IDisposable
    {
        event EventHandler<PacketReceivedEventArgs> OnPacketReceivedFromInbound;
        void StartCapture();
        void StopCapture();
        bool Started { get; }
        event EventHandler OnStopped;
        
        bool IsDnsServersSupported { get; }
        IPAddress[] DnsServers { get; set; }

        bool CanExcludeApps { get; }
        bool CanIncludeApps { get; }
        
        /// <summary>
        /// Unique id of excluded applications 
        /// </summary>
        string[] ExcludeApps { get; set; }
        
        /// <summary>
        /// Unique id of included applications
        /// </summary>
        string[] IncludeApps { get; set; }
        IpNetwork[] IncludeNetworks { get; set; }

        bool IsMtuSupported { get;}
        int Mtu { get; set; }

        bool CanProtectSocket { get; }
        void ProtectSocket(System.Net.Sockets.Socket socket);
        void SendPacketToInbound(IPPacket ipPacket);
        void SendPacketToInbound(IEnumerable<IPPacket> packets);

        bool CanSendPacketToOutbound { get; }
        void SendPacketToOutbound(IPPacket ipPacket);
        void SendPacketToOutbound(IEnumerable<IPPacket> ipPackets);
    }
}
