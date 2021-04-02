using PacketDotNet;
using SharpPcap.WinDivert;
using System;
using System.IO;
using System.IO.Compression;
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

        private IPNetwork[] _excludeNetworks;
        private IPNetwork[] _includeNetworks;
        private readonly WinDivertAddress LastWindivertAddress = new WinDivertAddress();

        protected readonly SharpPcap.WinDivert.WinDivertDevice _device;
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;

        public bool Started => _device.Started;

        private static void SetWinDivertDllFolder()
        {
            var dllFolderName = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var assemblyFolder = Path.GetDirectoryName(typeof(WinDivertDevice).Assembly.Location);
            var dllFolder = Path.Combine(assemblyFolder, dllFolderName);

            // extract WinDivert
            // I got sick trying to add it to nuget ad anative library in (x86/x64) folder, OOF!
            if (!File.Exists(Path.Combine(dllFolder, "WinDivert.dll")))
            {
                using var memStream = new MemoryStream(Resource.WinDivertLibZip);
                var tempLibFolder = Path.Combine(Path.GetTempPath(), "VpnHood-WinDivertDevice");
                dllFolder = Path.Combine(tempLibFolder, dllFolderName);
                // extract if file does not exists
                if (!File.Exists(Path.Combine(dllFolder, "WinDivert.dll")))
                {
                    using var zipArchive = new ZipArchive(memStream);
                    zipArchive.ExtractToDirectory(tempLibFolder, true);
                }
            }

            // set dll folder
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path.IndexOf(dllFolder + ";") == -1)
                Environment.SetEnvironmentVariable("PATH", dllFolder + ";" + path);
        }


        public WinDivertPacketCapture()
        {
            SetWinDivertDllFolder();

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
        public bool IsIncludeNetworksSupported => true;

        public IPNetwork[] ExcludeNetworks
        {
            get => _excludeNetworks;
            set
            {
                if (Started) 
                    throw new InvalidOperationException($"Can't set {nameof(ExcludeNetworks)} when {nameof(WinDivertPacketCapture)} is started!");
                _excludeNetworks = value;
            }
        }

        public IPNetwork[] IncludeNetworks
        {
            get => _includeNetworks;
            set
            {
                if (Started) throw new InvalidOperationException($"Can't set {nameof(IncludeNetworks)} when {nameof(WinDivertPacketCapture)} is started!");
                _includeNetworks = value;
            }
        }

        public void StartCapture()
        {
            if (Started)
                throw new InvalidOperationException("Device has been already started!");

            // add outbound; filter loopback
            var filter = "ip and outbound and !loopback";

            if (IncludeNetworks != null && IncludeNetworks.Length > 0)
            {
                var phrases = IncludeNetworks.Select(x => $"(ip.DstAddr>={x.FirstAddress} and ip.DstAddr<={x.LastAddress})").ToArray();
                var phrase = string.Join(" or ", phrases);
                filter += $" and (udp.DstPort==53 or ({phrase}))";
            }
            if (ExcludeNetworks != null && ExcludeNetworks.Length > 0)
            {
                var phrases = ExcludeNetworks.Select(x => $"(ip.DstAddr<{x.FirstAddress} or ip.DstAddr>{x.LastAddress})");
                var phrase = string.Join(" and ", phrases);
                filter += $" and (udp.DstPort==53 or ({phrase}))";
            }

            _device.Filter = filter;

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
