using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using SharpPcap.WinDivert;
using VpnHood.Common.Logging;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertPacketCapture : IPacketCapture
    {
        protected readonly SharpPcap.WinDivert.WinDivertDevice Device;

        private bool _disposed;
        private IpNetwork[]? _includeNetworks;
        private WinDivertHeader? _lastCaptureHeader;

        public WinDivertPacketCapture()
        {
            SetWinDivertDllFolder();

            // initialize devices
            Device = new SharpPcap.WinDivert.WinDivertDevice { Flags = 0 };
            Device.OnPacketArrival += Device_OnPacketArrival;
        }

        public event EventHandler<PacketReceivedEventArgs>? OnPacketReceivedFromInbound;
        public event EventHandler? OnStopped;

        public bool Started => Device.Started;
        public virtual bool CanSendPacketToOutbound => true;

        public virtual bool IsDnsServersSupported => false;

        public virtual IPAddress[]? DnsServers
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public virtual bool CanProtectSocket => false;

        public virtual void ProtectSocket(Socket socket)
        {
            throw new NotSupportedException(
                $"{nameof(ProcessPacketReceivedFromInbound)} is not supported by {nameof(WinDivertDevice)}");
        }

        public void SendPacketToInbound(IPPacket[] ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket, false);
        }

        public void SendPacketToInbound(IPPacket ipPacket)
        {
            SendPacket(ipPacket, false);
        }

        public void SendPacketToOutbound(IPPacket ipPacket)
        {
            SendPacket(ipPacket, true);
        }

        public void SendPacketToOutbound(IPPacket[] ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket, true);
        }

        public IpNetwork[]? IncludeNetworks
        {
            get => _includeNetworks;
            set
            {
                if (Started)
                    throw new InvalidOperationException(
                        $"Can't set {nameof(IncludeNetworks)} when {nameof(WinDivertPacketCapture)} is started!");
                _includeNetworks = value;
            }
        }

        private string Ip(IpRange ipRange)
        {
            return ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ip";
        }

        public void StartCapture()
        {
            if (Started)
                throw new InvalidOperationException("Device has been already started!");

            // create include and exclude phrases
            var phraseX = "true";
            if (IncludeNetworks != null)
            {
                var ipRanges = IpNetwork.ToIpRange(IncludeNetworks);
                var phrases = ipRanges.Select(x => x.FirstIpAddress.Equals(x.LastIpAddress)
                    ? $"{Ip(x)}.DstAddr=={x.FirstIpAddress}"
                    : $"({Ip(x)}.DstAddr>={x.FirstIpAddress} and {Ip(x)}.DstAddr<={x.LastIpAddress})");
                var phrase = string.Join(" or ", phrases);
                phraseX += $" and ({phrase})";
            }

            // add outbound; filter loopback
            var filter = $"(ip or ipv6) and outbound and !loopback and (udp.DstPort==53 or ({phraseX}))";
            // filter = $"(ip or ipv6) and outbound and !loopback and (protocol!=6 or tcp.DstPort!=3389) and (protocol!=6 or tcp.SrcPort!=3389) and (udp.DstPort==53 or ({phraseX}))";
            filter = filter.Replace("ipv6.DstAddr>=::", "ipv6"); // WinDivert bug
            try
            {
                Device.Filter = filter;
                Device.Open(new DeviceConfiguration());
                Device.StartCapture();
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception(
                        "Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.", ex);
                throw;
            }
        }

        public void StopCapture()
        {
            if (!Started)
                return;

            Device.StopCapture();
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopCapture();
                Device.Dispose();
                _disposed = true;
            }
        }

        private static void SetWinDivertDllFolder()
        {
            // I got sick trying to add it to nuget as a native library in (x86/x64) folder, OOF!
            var tempLibFolder = Path.Combine(Path.GetTempPath(), "VpnHood-WinDivertDevice");
            var dllFolderPath = Environment.Is64BitOperatingSystem
                ? Path.Combine(tempLibFolder, "x64")
                : Path.Combine(tempLibFolder, "x86");
            var requiredFiles = Environment.Is64BitOperatingSystem
                ? new[] { "WinDivert.dll", "WinDivert64.sys" }
                : new[] { "WinDivert.dll", "WinDivert32.sys", "WinDivert64.sys" };

            // extract WinDivert
            var checkFiles = requiredFiles.Select(x => Path.Combine(dllFolderPath, x));
            if (checkFiles.Any(x => !File.Exists(x)))
            {
                using var memStream = new MemoryStream(Resource.WinDivertLibZip);
                using var zipArchive = new ZipArchive(memStream);
                zipArchive.ExtractToDirectory(tempLibFolder, true);
            }

            // set dll folder
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (path.IndexOf(dllFolderPath + ";", StringComparison.Ordinal) == -1)
                Environment.SetEnvironmentVariable("PATH", dllFolderPath + ";" + path);
        }

        private void Device_OnPacketArrival(object sender, PacketCapture e)
        {
            var rawPacket = e.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var ipPacket = packet.Extract<IPPacket>();

            _lastCaptureHeader = (WinDivertHeader)e.Header;
            ProcessPacketReceivedFromInbound(ipPacket);
        }

        protected virtual void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
        {
            try
            {
                var eventArgs = new PacketReceivedEventArgs(new[] { ipPacket }, this);
                OnPacketReceivedFromInbound?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, $"Error in processing packet {ipPacket}! Error: {ex}");
            }
        }

        private void SendPacket(IPPacket ipPacket, bool outbound)
        {
            if (_lastCaptureHeader == null)
                throw new InvalidOperationException("Could not send any data without receiving a packet!");

            // send by a device
            _lastCaptureHeader.Flags = outbound ? WinDivertPacketFlags.Outbound : 0;
            Device.SendPacket(ipPacket.Bytes, _lastCaptureHeader);
        }

        #region Applications Filter

        public bool CanExcludeApps => false;
        public bool CanIncludeApps => false;

        public string[]? ExcludeApps
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string[]? IncludeApps
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public bool IsMtuSupported => false;

        public int Mtu
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion
    }
}