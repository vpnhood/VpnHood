using PacketDotNet;
using System;

namespace VpnHood.Client.Device
{
    public interface IPacketCapture : IDisposable
    {
        void StartCapture();
        void StopCapture();
        bool Started { get; }
        event EventHandler OnStopped;

        bool IsExcludeApplicationsSupported { get; }
        bool IsIncludeApplicationsSupported { get; }
        
        /// <summary>
        /// Unique id of excluded applications 
        /// </summary>
        string[] ExcludeApplications { get; set; }
        
        /// <summary>
        /// Unique id of included applications
        /// </summary>
        string[] IncludeApplications { get; set; }

        bool IsExcludeNetworksSupported { get; }
        bool IsIncludeNetworksSupported { get; }
        
        /// <summary>
        /// packets sent to this ip will not be captured
        /// </summary>
        IPNetwork[] ExcludeNetworks { get; set; }

        /// <summary>
        /// if set, then only packet sent to this network will be captured
        /// </summary>
        IPNetwork[] IncludeNetworks { get; set; }

        void ProtectSocket(System.Net.Sockets.Socket socket);
        void SendPacketToInbound(IPPacket packet);
        event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
    }
}
