using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap.WinDivert;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertPacketCapture : IPacketCapture
    {
        private IpNetwork[] _includeNetworks;
        private WinDivertHeader _lastCaptureHeader;

        protected readonly SharpPcap.WinDivert.WinDivertDevice _device;
        public event EventHandler<PacketReceivedEventArgs> OnPacketReceivedFromInbound;
        public event EventHandler OnStopped;

        public bool Started => _device.Started;

        private static void SetWinDivertDllFolder()
        {
            // I got sick trying to add it to nuget ad anative library in (x86/x64) folder, OOF!
            var tempLibFolder = Path.Combine(Path.GetTempPath(), "VpnHood-WinDivertDevice");
            var dllFolderPath = Environment.Is64BitOperatingSystem ? Path.Combine(tempLibFolder, "x64") : Path.Combine(tempLibFolder, "x86");
            var requiredFiles = Environment.Is64BitOperatingSystem
                ? new string[] { "WinDivert.dll", "WinDivert64.sys" }
                : new string[] { "WinDivert.dll", "WinDivert32.sys", "WinDivert64.sys" };

            // extract WinDivert
            var checkFiles = requiredFiles.Select(x => Path.Combine(dllFolderPath, x));
            if (checkFiles.Any(x => !File.Exists(x)))
            {
                using var memStream = new MemoryStream(Resource.WinDivertLibZip);
                using var zipArchive = new ZipArchive(memStream);
                zipArchive.ExtractToDirectory(tempLibFolder, true);
            }

            // set dll folder
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path.IndexOf(dllFolderPath + ";") == -1)
                Environment.SetEnvironmentVariable("PATH", dllFolderPath + ";" + path);
        }

        private readonly EventWaitHandle _newPacketEvent = new EventWaitHandle(false, EventResetMode.AutoReset);

        public WinDivertPacketCapture()
        {
            SetWinDivertDllFolder();

            // initialize devices
            _device = new SharpPcap.WinDivert.WinDivertDevice { Flags = 0 };
            _device.OnPacketArrival += Device_OnPacketArrival;
        }

        private void Device_OnPacketArrival(object sender, SharpPcap.PacketCapture e)
        {
            var rawPacket = e.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var ipPacket = packet.Extract<IPPacket>();

            _lastCaptureHeader = (WinDivertHeader)e.Header;
            ProcessPacket(ipPacket);
        }

        private readonly IPPacket[] _receivedPackets = new IPPacket[1];
        protected virtual void ProcessPacket(IPPacket ipPacket)
        {
            try
            {
                _receivedPackets[0] = ipPacket;
                var eventArgs = new PacketReceivedEventArgs(_receivedPackets, this);
                OnPacketReceivedFromInbound?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, $"Error in processing packet {ipPacket}! Error: {ex}");
            }
        }

        public void Dispose()
        {
            StopCapture();
            _device.Dispose();
        }

        public void SendPacketToInbound(IEnumerable<IPPacket> ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket, false);
        }

        public void SendPacketToInbound(IPPacket ipPacket)
            => SendPacket(ipPacket, false);

        public void SendPacketToOutbound(IPPacket ipPacket)
            => SendPacket(ipPacket, true);

        public void SendPacketToOutbound(IEnumerable<IPPacket> ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket, true);
        }

        private void SendPacket(IPPacket ipPacket, bool outbound)
        {
            // send by a device
            _lastCaptureHeader.Flags = outbound ? WinDivertPacketFlags.Outbound : 0;
            _device.SendPacket(ipPacket.Bytes, _lastCaptureHeader);
        }

        public IPAddress[] RouteAddresses { get; set; }

        public IpNetwork[] IncludeNetworks
        {
            get => _includeNetworks;
            set
            {
                if (Started)
                    throw new InvalidOperationException($"Can't set {nameof(IncludeNetworks)} when {nameof(WinDivertPacketCapture)} is started!");
                _includeNetworks = value;
            }
        }

        #region Applications Filter
        public bool CanExcludeApps => false;
        public bool CanIncludeApps => false;
        public string[] ExcludeApps { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public string[] IncludeApps { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public bool IsMtuSupported => false;
        public int Mtu { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        #endregion

        public void StartCapture()
        {
            if (Started)
                throw new InvalidOperationException("Device has been already started!");

            // create include and exclude phrases
            var phraseX = "true";
            if (IncludeNetworks != null)
            {
                var ipRanges = IpNetwork.ToIpRange(IncludeNetworks);
                var phrases = ipRanges.Select(x => $"(ip.DstAddr>={x.FirstIpAddress} and ip.DstAddr<={x.LastIpAddress})").ToArray();
                var phrase = string.Join(" or ", phrases);
                phraseX += $" and ({phrase})";
            }

            // add outbound; filter loopback
            var filter = $"ip and outbound and !loopback and (udp.DstPort==53 or ({phraseX}))";
            try
            {
                _device.Filter = filter;
                _device.Open(new SharpPcap.DeviceConfiguration());
                _device.StartCapture();
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.", ex);
                throw;
            }
        }

        public void StopCapture()
        {
            if (!Started)
                return;

            _device.StopCapture();
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        public virtual bool CanSendPacketToOutbound => true;

        public virtual bool IsDnsServersSupported => false;

        public virtual IPAddress[] DnsServers { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public virtual bool CanProtectSocket => false;
        public virtual void ProtectSocket(System.Net.Sockets.Socket socket) => throw new NotSupportedException($"{nameof(ProcessPacket)} is not supported by {nameof(WinDivertDevice)}");

    }
}
