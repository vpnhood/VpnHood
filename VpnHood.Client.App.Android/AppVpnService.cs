using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using VpnHood.Client.App;
using VpnHood.Logger;
using Java.IO;
using Java.Net;
using Java.Nio.Channels;
using Java.Security;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace VpnHood.Client.Droid
{
    [Service(Label = VpnServiceName, Permission = Manifest.Permission.BindVpnService)]
    class AppVpnService : VpnService, IDeviceInbound
    {
        public const string VpnServiceName = "VpnHoodService";

        private ParcelFileDescriptor _mInterface;
        private FileInputStream _inStream; // Packets to be sent are queued in this input stream.
        private FileOutputStream _outStream; // Packets received need to be written to this output stream.

        public bool Started => _mInterface != null;
        public IPAddress ProtectedIpAddress { get; set; }

        public AppVpnService()
        {
        }


        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {
            if (!Started)
                AndroidApp.Current.InvokeDeviceReady(this);

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

            //Configure the TUN and get the interface.
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
                        OnPacketArrivalFromInbound?.Invoke(this, new DevicePacketArrivalEventArgs(ipPacket, this));
                }
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception ex)
            {
                if (!Util.IsSocketClosedException(ex))
                    Logger.Logger.Current.LogError($"ReadingPacketTask: {ex}");
            }

            if (Started)
                Close();

            return Task.FromResult(0);
        }

        public event EventHandler<DevicePacketArrivalEventArgs> OnPacketArrivalFromInbound;
        public event EventHandler OnStopped;

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

            Logger.Logger.Current.LogTrace("Stopping VPN Service...");
            Close();
        }

        public override void OnDestroy()
        {
            Logger.Logger.Current.LogTrace("VpnService has been destroyed!");
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

            Logger.Logger.Current.LogTrace("Closing VpnService...");

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