using PacketDotNet;
using SharpPcap.WinDivert;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

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
        private const int DEVICE_COUNT = 1;

        protected readonly SharpPcap.WinDivert.WinDivertDevice[] _devices;
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;

        public bool Started => _devices[0].Started;

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
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path.IndexOf(dllFolder + ";") == -1)
                Environment.SetEnvironmentVariable("PATH", dllFolder + ";" + path);
        }

        private readonly EventWaitHandle _newPacketEvent = new EventWaitHandle(false, EventResetMode.AutoReset);

        public WinDivertPacketCapture()
        {
            SetWinDivertDllFolder();

            // initialize devices
            _devices = new SharpPcap.WinDivert.WinDivertDevice[DEVICE_COUNT];
            for (var i = 0; i < _devices.Length; i++)
            {
                var device = new SharpPcap.WinDivert.WinDivertDevice
                {
                    Flags = 0
                };
                device.OnPacketArrival += Device_OnPacketArrival;
                _devices[i] = device;

                //Task.Run(() =>
                //{
                //    while (true)
                //    {
                //        _newPacketEvent.WaitOne();

                //        // wait
                //        if (_queue.TryDequeue(out WinDivertPacket winDivertPacket))
                //            device.SendPacket(winDivertPacket);
                //    }
                //});
            }
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
            OnPacketArrivalFromInbound?.Invoke(this, new PacketCaptureArrivalEventArgs(new[] { ipPacket }, this));
        }

        public void Dispose()
        {
            StopCapture();
        }

        public void SendPacketToInbound(IPPacket[] ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket, false);
        }

        private readonly ConcurrentQueue<WinDivertPacket> _queue = new ConcurrentQueue<WinDivertPacket>();
        protected void SendPacket(IPPacket ipPacket, bool outbound)
        {
            var divertPacket = new WinDivertPacket(ipPacket.BytesSegment)
            {
                InterfaceIndex = LastWindivertAddress.InterfaceIndex,
                SubInterfaceIndex = LastWindivertAddress.SubInterfaceIndex,
                Flags = outbound ? WinDivertPacketFlags.Outbound : 0
            };

            // send by a device
            //_queue.Enqueue(divertPacket);
            //_newPacketEvent.Set();
            _devices[0].SendPacket(divertPacket);
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

        #region Applications Filter
        public bool IsExcludeApplicationsSupported => false;
        public bool IsIncludeApplicationsSupported => false;
        public string[] ExcludeApplications { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public string[] IncludeApplications { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        #endregion

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

            foreach (var device in _devices)
            {
                device.Filter = filter;

                try
                {
                    device.Open();
                }
                catch (Exception ex)
                {
                    if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception("Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.", ex);
                    throw;
                }

                device.StartCapture();
            }
        }

        public void StopCapture()
        {
            foreach (var device in _devices)
            {
                if (device.Started)
                    device.StopCapture();
                device.Close();
            }
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ProtectSocket(System.Net.Sockets.Socket socket)
        {
        }
    }
}
