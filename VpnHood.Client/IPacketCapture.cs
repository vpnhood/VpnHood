using PacketDotNet;
using System;
using System.Net;

namespace VpnHood.Client
{
    public interface IPacketCapture : IDisposable
    {
        void StartCapture();
        void StopCapture();
        bool Started { get; }
        event EventHandler OnStopped;

        /// <summary>
        /// package sent by this ip will not be feedback to the packet capture
        /// </summary>
        IPAddress ProtectedIpAddress { get; set; }
        void ProtectSocket(System.Net.Sockets.Socket socket);
        void SendPacketToInbound(IPPacket packet);
        event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
    }
}
