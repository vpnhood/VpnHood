using System;
using System.Net;

namespace VpnHood.Client
{
    public interface IDevice : IDisposable
    {
        /// <summary>
        /// package sent by it will not be feedback to the vpn service.
        /// </summary>
        IPAddress ProtectedIpAddress { get; set; }
        void StartCapture();
        void StopCapture();
        bool Started { get; }
        event EventHandler OnStopped;
    }

}
