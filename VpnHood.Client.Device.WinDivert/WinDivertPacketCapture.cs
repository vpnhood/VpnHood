using PacketDotNet;
using SharpPcap.WinDivert;
using System;
using System.Linq;
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
        private IPNetwork[] _excludeNetworks;
        private IPNetwork[] _includeNetworks;
        private readonly WinDivertAddress LastWindivertAddress = new WinDivertAddress();

        protected readonly SharpPcap.WinDivert.WinDivertDevice _device;
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;

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

        public IPAddress[] RouteAddresses { get; set; }

        public bool IsExcludeNetworksSupported => true;
        public bool IsNetworkPrefixLengthSupported => false;
        public bool IsIncludeNetworksSupported => true;

        public IPNetwork[] ExcludeNetworks
        {
            get => _excludeNetworks;
            set
            {
                if (value != null && value.Any(x => x.PrefixLength != 32))
                    throw new NotSupportedException($"{nameof(IPNetwork.PrefixLength)} is not supported! It must be 32.");
                _excludeNetworks = value;
            }
        }

        public IPNetwork[] IncludeNetworks
        {
            get => _includeNetworks;
            set
            {
                if (value != null && value.Any(x => x.PrefixLength != 32))
                    throw new NotSupportedException($"{nameof(IPNetwork.PrefixLength)} is not supported! It must be 32.");
                _includeNetworks = value;
            }
        }

        public void StartCapture()
        {
            if (Started)
                throw new InvalidOperationException("Device has been already started!");

            // add outbound; filter loopback
            _device.Filter = "ip and outbound and !loopback";

            if (IncludeNetworks != null)
            {
                var ips = IncludeNetworks.Select(x => x.Prefix.ToString());
                var filter = string.Join($" and ip.DstAddr==", ips);
                _device.Filter += $" and ip.DstAddr=={filter}";
            }
            if (ExcludeNetworks != null)
            {
                var ips = ExcludeNetworks.Select(x => x.Prefix.ToString());
                var filter = string.Join($" and ip.DstAddr!=", ips);
                _device.Filter += $" and ip.DstAddr!={filter}";
            }

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
