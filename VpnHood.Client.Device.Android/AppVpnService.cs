using System;
using System.Net;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Java.IO;
using PacketDotNet;
using Microsoft.Extensions.Logging;
using VpnHood.Logging;
using VpnHood.Common;
using System.Linq;
using System.Collections.Generic;

namespace VpnHood.Client.Device.Android
{

    [Service(Label = VpnServiceName, Permission = Manifest.Permission.BindVpnService)]
    [IntentFilter(new[] { "android.net.VpnService" })]
    class AppVpnService : VpnService, IPacketCapture
    {
        private ParcelFileDescriptor _mInterface;
        private FileInputStream _inStream; // Packets to be sent are queued in this input stream.
        private FileOutputStream _outStream; // Packets received need to be written to this output stream.
        private int _mtu;
        public const string VpnServiceName = "VpnHood";
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;
        public bool Started => _mInterface != null;

        public IPNetwork[] ExcludeNetworks { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public IPNetwork[] IncludeNetworks { get => throw new NotSupportedException(); set => throw new NotImplementedException(); }
        public bool IsExcludeNetworksSupported => false;
        public bool IsIncludeNetworksSupported => false;

        #region Application Filter
        public bool IsExcludeApplicationsSupported => true;
        public bool IsIncludeApplicationsSupported => true;
        public string[] ExcludeApplications { get; set; } = Array.Empty<string>();
        public string[] IncludeApplications { get; set; } = Array.Empty<string>();
        #endregion

        public AppVpnService()
        {
        }

        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {
            if (!Started)
            {
                if (AndroidDevice.Current == null) throw new Exception($"{nameof(AndroidDevice)} has not been initialized");
                AndroidDevice.Current.OnServiceStartCommand(this, intent);
            }
            return StartCommandResult.Sticky;
        }

        public bool IsMtuSupported => true;
        public int Mtu
        {
            get => _mtu;
            set
            {
                if (Started)
                    throw new InvalidOperationException($"Could not set {nameof(Mtu)} while {nameof(IPacketCapture)} is started!");
                _mtu = value;
            }
        }

        public void StartCapture()
        {
            var builder = new Builder(this)
                .SetBlocking(true)
                .SetSession(VpnServiceName)
                .AddAddress("192.168.0.100", 24)
                .AddDnsServer("8.8.8.8")
                .AddRoute("0.0.0.0", 0);

            // set mtu
            if (Mtu != 0)
                builder.SetMtu(Mtu);

            var packageName = ApplicationContext.PackageName;

            // Applications Filter
            if (IncludeApplications != null && IncludeApplications.Length > 0)
            {
                foreach (var app in IncludeApplications)
                    builder.AddAllowedApplication(app);
                if (IncludeApplications.FirstOrDefault(x => x == packageName) == null)
                    builder.AddAllowedApplication(packageName);
            }

            if (ExcludeApplications != null && ExcludeApplications.Length > 0)
                foreach (var app in ExcludeApplications.Where(x => x != packageName))
                    builder.AddDisallowedApplication(app);

            // try to stablish the connection
            _mInterface = builder.Establish();

            //Packets to be sent are queued in this input stream.
            _inStream = new FileInputStream(_mInterface.FileDescriptor);

            //b. Packets received need to be written to this output stream.
            _outStream = new FileOutputStream(_mInterface.FileDescriptor);

            Task.Run(ReadingPacketTask);
        }

        private Task ReadingPacketTask()
        {
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
                OnPacketArrivalFromInbound?.Invoke(this, new PacketCaptureArrivalEventArgs(new[] { ipPacket }, this));
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, $"Error in processing packet {ipPacket}! Error: {ex}");
            }
        }

        public void SendPacketToInbound(IPPacket[] ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                _outStream.Write(ipPacket.Bytes);
        }

        public void ProtectSocket(System.Net.Sockets.Socket socket)
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

        public override void OnDestroy()
        {
            VhLogger.Instance.LogTrace("VpnService has been destroyed!");
            base.OnDestroy(); // must called first

            Close();
            OnStopped?.Invoke(this, EventArgs.Empty);
        }

        void IDisposable.Dispose()
        {
            // This object should not be disposed, never call parent
            Close();
        }

        private void Close()
        {
            if (!Started)
                return;

            VhLogger.Instance.LogTrace("Closing VpnService...");

            _inStream?.Dispose();
            _inStream = null;

            _outStream?.Dispose();
            _outStream = null;
            _mInterface?.Close();
            _mInterface?.Dispose();
            _mInterface = null;

            StopSelf();

        }
    }
}