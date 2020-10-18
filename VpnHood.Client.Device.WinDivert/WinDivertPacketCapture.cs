using PacketDotNet;
using SharpPcap.WinDivert;
using System;
using System.Net;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertPacketCapture : IPacketCapture
    {
        private class WinDivertAddress
        {
            public uint InterfaceIndex;
            public uint SubInterfaceIndex;
        }

        //todo: wait for nuget; must be 5.3.0 or greater; 
        protected readonly SharpPcap.WinDivert.WinDivertDevice _device; 
        private readonly WinDivertAddress LastWindivertAddress = new WinDivertAddress();
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;
        
        public IPAddress ProtectedIpAddress { get; set; }
        public bool Started => _device.Started;

        public WinDivertPacketCapture()
        {
            _device = new SharpPcap.WinDivert.WinDivertDevice
            {
                Flags = 0
            };
            _device.OnPacketArrival += Device_OnPacketArrival;
        }

        private void Device_OnPacketArrival(object sender, SharpPcap.CaptureEventArgs e)
        {
            var windDivertPacket = e.Packet as WinDivertCapture ?? throw new Exception("Unexpected non WinDivert packet!");
            var ipPacket = windDivertPacket.GetPacket().Extract<IPPacket>();
            LastWindivertAddress.InterfaceIndex = windDivertPacket.InterfaceIndex;
            LastWindivertAddress.SubInterfaceIndex = windDivertPacket.SubInterfaceIndex;
            ProcessPacket(ipPacket);
        }

        protected virtual void ProcessPacket(IPPacket ipPacket)
        {
            OnPacketArrivalFromInbound?.Invoke(this, new PacketCaptureArrivalEventArgs(ipPacket, this));
        }

        public void Dispose()
        {
            if (_device.Started)
                StopCapture();
            _device.Close();
        }

        public void SendPacketToInbound(IPPacket ipPacket)
        {
            SendPacket(ipPacket, false);
        }

        protected void SendPacket(IPPacket ipPacket, bool outbound)
        {
            var divertPacket = new WinDivertPacket(ipPacket.BytesSegment)
            {
                InterfaceIndex = LastWindivertAddress.InterfaceIndex,
                SubInterfaceIndex = LastWindivertAddress.SubInterfaceIndex,
                Flags = outbound ? WinDivertPacketFlags.Outbound : 0
            };
            _device.SendPacket(divertPacket);
        }

        public void StartCapture()
        {
            if (Started)
                throw new InvalidOperationException("Device has been already started!");

            _device.Filter = "ip and outbound and !loopback";
            if (ProtectedIpAddress != null)
                _device.Filter += $" and ip.DstAddr!={ProtectedIpAddress}";

            try
            {
                _device.Open();
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.", ex);
                throw;
            }

            _device.StartCapture();
        }

        public void StopCapture()
        {
            _device.StopCapture();
            _device.Close();
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ProtectSocket(System.Net.Sockets.Socket socket)
        {
        }
    }
}
