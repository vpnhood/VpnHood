using System;
using System.Net;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Runtime;
using VpnHood.Loggers;
using Java.IO;
using PacketDotNet;
using Microsoft.Extensions.Logging;
using Android.Content.PM;

namespace VpnHood.Client.Device.Android
{

    [Service(Label = VpnServiceName, Permission = Manifest.Permission.BindVpnService)]
    [IntentFilter(new[] { "android.net.VpnService" })]
    class AppVpnService : VpnService, IPacketCapture
    {
        private ParcelFileDescriptor _mInterface;
        private FileInputStream _inStream; // Packets to be sent are queued in this input stream.
        private FileOutputStream _outStream; // Packets received need to be written to this output stream.

        public const string VpnServiceName = "VpnHood";
        public event EventHandler<PacketCaptureArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;
        public bool Started => _mInterface != null;
        public IPAddress ProtectedIpAddress { get; set; }

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

        public void StartCapture()
        {
            var builder = new Builder(this)
                .SetBlocking(true)
                .SetSession(VpnServiceName)
                .AddAddress("192.168.0.100", 24)
                .AddDnsServer("8.8.8.8")
                .AddRoute("0.0.0.0", 0);

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
                        OnPacketArrivalFromInbound?.Invoke(this, new PacketCaptureArrivalEventArgs(ipPacket, this));
                }
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception ex)
            {
                if (!Util.IsSocketClosedException(ex))
                    Logger.Current.LogError($"ReadingPacketTask: {ex}");
            }

            if (Started)
                Close();

            return Task.FromResult(0);
        }

        public void SendPacketToInbound(IPPacket packet)
        {
            _outStream.Write(packet.Bytes);
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

            Logger.Current.LogTrace("Stopping VPN Service...");
            Close();
        }

        public override void OnDestroy()
        {
            Logger.Current.LogTrace("VpnService has been destroyed!");
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

            Logger.Current.LogTrace("Closing VpnService...");

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