#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Java.IO;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.Client.Device.Android
{
    [Service(Label = VpnServiceName, Permission = Manifest.Permission.BindVpnService)]
    [IntentFilter(new[] {"android.net.VpnService"})]
    internal class AppVpnService : VpnService, IPacketCapture
    {
        public const string VpnServiceName = "VpnHood";
        private IPAddress[]? _dnsServers = {IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")};
        private FileInputStream? _inStream; // Packets to be sent are queued in this input stream.
        private ParcelFileDescriptor? _mInterface;
        private int _mtu;
        private FileOutputStream? _outStream; // Packets received need to be written to this output stream.

        public event EventHandler<PacketReceivedEventArgs>? OnPacketReceivedFromInbound;
        public event EventHandler? OnStopped;
        public bool Started => _mInterface != null;
        public IpNetwork[]? IncludeNetworks { get; set; }
        public bool CanSendPacketToOutbound => false;

        public bool IsMtuSupported => true;

        public int Mtu
        {
            get => _mtu;
            set
            {
                if (Started)
                    throw new InvalidOperationException(
                        $"Could not set {nameof(Mtu)} while {nameof(IPacketCapture)} is started!");
                _mtu = value;
            }
        }

        public bool IsDnsServersSupported => true;

        public IPAddress[]? DnsServers
        {
            get => _dnsServers;
            set
            {
                if (Started)
                    throw new InvalidOperationException(
                        $"Could not set {nameof(DnsServers)} while {nameof(IPacketCapture)} is started!");
                _dnsServers = value;
            }
        }

        public void StartCapture()
        {
            var builder = new Builder(this)
                .SetBlocking(true)
                .SetSession(VpnServiceName)
                .AddAddress("192.168.0.100", 24);

            // dnsServers
            if (DnsServers is {Length: > 0})
                foreach (var dnsServer in DnsServers)
                    builder.AddDnsServer(dnsServer.ToString());
            else
                builder.AddDnsServer("8.8.8.8");

            // Routes
            if (IncludeNetworks?.Length > 0)
                foreach (var network in IncludeNetworks)
                    builder.AddRoute(network.Prefix.ToString(), network.PrefixLength);
            else
                builder.AddRoute("0.0.0.0", 0);


            // set mtu
            if (Mtu != 0)
                builder.SetMtu(Mtu);

            // AppFilter
            AddAppFilter(builder);

            // try to establish the connection
            _mInterface = builder.Establish() ?? throw new Exception("Could not establish VpnService");

            //Packets to be sent are queued in this input stream.
            _inStream = new FileInputStream(_mInterface.FileDescriptor);

            //Packets received need to be written to this output stream.
            _outStream = new FileOutputStream(_mInterface.FileDescriptor);

            Task.Run(ReadingPacketTask);
        }

        public void SendPacketToInbound(IPPacket ipPacket)
        {
            _outStream?.Write(ipPacket.Bytes);
        }

        public void SendPacketToInbound(IEnumerable<IPPacket> ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                _outStream?.Write(ipPacket.Bytes);
        }

        public void SendPacketToOutbound(IEnumerable<IPPacket> ipPackets)
        {
            throw new NotSupportedException();
        }

        public void SendPacketToOutbound(IPPacket ipPacket)
        {
            throw new NotSupportedException();
        }

        public bool CanProtectSocket => true;

        public void ProtectSocket(Socket socket)
        {
            if (!Protect(socket.Handle.ToInt32()))
                throw new Exception("Could not protect socket!");
        }

        public void StopCapture()
        {
            if (!Started)
                return;

            VhLogger.Instance.LogTrace("Stopping VPN Service...");
            Close();
        }

        void IDisposable.Dispose()
        {
            // The parent should not be disposed, never call parent dispose
            Close();
        }

        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent? intent, [GeneratedEnum] StartCommandFlags flags,
            int startId)
        {
            if (!Started)
            {
                if (AndroidDevice.Current == null)
                    throw new Exception($"{nameof(AndroidDevice)} has not been initialized");
                AndroidDevice.Current.OnServiceStartCommand(this, intent);
            }

            return StartCommandResult.Sticky;
        }

        private void AddAppFilter(Builder builder)
        {
            // Applications Filter
            if (IncludeApps?.Length > 0)
            {
                // make sure to add current app if an allowed app exists
                var packageName = ApplicationContext?.PackageName ??
                                  throw new Exception("Could not get the app PackageName!");
                builder.AddAllowedApplication(packageName);

                // add user apps
                foreach (var app in IncludeApps.Where(x => x != packageName))
                    try
                    {
                        builder.AddAllowedApplication(app);
                    }
                    catch (Exception ex)
                    {
                        VhLogger.Instance.LogError(ex, $"Could not add allowed app: {app}");
                    }
            }

            if (ExcludeApps?.Length > 0)
            {
                var packageName = ApplicationContext?.PackageName ??
                                  throw new Exception("Could not get the app PackageName!");
                foreach (var app in ExcludeApps.Where(x => x != packageName))
                    try
                    {
                        builder.AddAllowedApplication(app);
                    }
                    catch (Exception ex)
                    {
                        VhLogger.Instance.LogError(ex, $"Could not add allowed app: {app}");
                    }
            }
        }

        private Task ReadingPacketTask()
        {
            if (_inStream == null)
                throw new ArgumentNullException(nameof(_inStream));

            try
            {
                var buf = new byte[short.MaxValue];
                while (_inStream.Read(buf) > 0)
                {
                    var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buf)?.Extract<IPv4Packet>();
                    if (ipPacket != null)
                        ProcessPacket(ipPacket);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                if (!Util.IsSocketClosedException(ex))
                    VhLogger.Instance.LogError($"ReadingPacketTask: {ex}");
            }

            if (Started)
                Close();

            return Task.FromResult(0);
        }

        protected virtual void ProcessPacket(IPPacket ipPacket)
        {
            try
            {
                OnPacketReceivedFromInbound?.Invoke(this, new PacketReceivedEventArgs(new[] {ipPacket}, this));
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, $"Error in processing packet {ipPacket}! Error: {ex}");
            }
        }

        public override void OnDestroy()
        {
            VhLogger.Instance.LogTrace("VpnService has been destroyed!");
            base.OnDestroy(); // must called first

            Close();
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        private void Close()
        {
            if (!Started)
                return;

            VhLogger.Instance.LogTrace("Closing VpnService...");

            _inStream?.Dispose();
            _outStream?.Dispose();
            _mInterface?.Close();
            _mInterface?.Dispose();

            StopSelf();
        }

        #region Application Filter

        public bool CanExcludeApps => true;
        public bool CanIncludeApps => true;
        public string[]? ExcludeApps { get; set; }
        public string[]? IncludeApps { get; set; }

        #endregion
    }
}