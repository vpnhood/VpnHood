using System.Net;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Java.IO;
using Java.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;
using ProtocolType = PacketDotNet.ProtocolType;
using Socket = System.Net.Sockets.Socket;

namespace VpnHood.Core.Client.Device.Droid;

[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = false,
    //Process = ":vpnhood_process",
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidVpnAdapter : VpnService, IVpnAdapter
{
    private FileInputStream? _inStream; // Packets to be sent are queued in this input stream.
    private ParcelFileDescriptor? _mInterface;
    private FileOutputStream? _outStream; // Packets received need to be written to this output stream.
    private readonly ConnectivityManager? _connectivityManager = ConnectivityManager.FromContext(Application.Context);
    internal static TaskCompletionSource<AndroidVpnAdapter>? StartServiceTaskCompletionSource { get; set; }

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public bool Started => _mInterface != null;
    private bool _isServiceStarted;
    public bool CanSendPacketToOutbound => false;
    public bool IsMtuSupported => true;

    public IpNetwork[] PrivateIpNetworks { get; set; } = [];

    public bool IsDnsServersSupported => true;

    public void Init()
    {
    }

    public void StartCapture(VpnAdapterOptions options)
    {
        CloseVpn();

        var builder = new Builder(this)
            .SetBlocking(true);

        // Private IP Networks
        if (options.VirtualIpNetworkV4 != null)
            builder.AddAddress(options.VirtualIpNetworkV4.Prefix.ToString(), options.VirtualIpNetworkV4.PrefixLength);

        if (options.VirtualIpNetworkV6 != null)
            builder.AddAddress(options.VirtualIpNetworkV6.Prefix.ToString(), options.VirtualIpNetworkV6.PrefixLength);

        // Android 10 and above
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            builder.SetMetered(false);

        // Session Name
        if (!string.IsNullOrWhiteSpace(options.SessionName))
            builder.SetSession(options.SessionName.Trim());

        // MTU
        if (options.Mtu != null)
            builder.SetMtu(options.Mtu.Value);

        // Add Routes
        foreach (var network in options.IncludeNetworks)
            builder.AddRoute(network.Prefix.ToString(), network.PrefixLength);

        // AppFilter
        var appPackageName =
            ApplicationContext?.PackageName ?? throw new Exception("Could not get the app PackageName!");
        AddAppFilter(builder, includeApps: options.IncludeApps, excludeApps: options.ExcludeApps,
            appPackageName: appPackageName);

        // DNS Servers
        AddDnsServers(builder, options.DnsServers, PrivateIpNetworks.Any(x => x.IsIpV6));

        // try to establish the connection
        _mInterface = builder.Establish() ?? throw new Exception("Could not establish VpnService.");

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

    public void SendPacketToInbound(IList<IPPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            _outStream?.Write(ipPacket.Bytes);
        }
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
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
        VhLogger.Instance.LogTrace("Stopping VPN Service...");
        StopVpnService();
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent,
        [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        // close Vpn if it is already started
        CloseVpn();

        // post vpn service start command
        try {
            AndroidDevice.Instance.OnServiceStartCommand(this, intent);
        }
        catch (Exception ex) {
            StartServiceTaskCompletionSource?.TrySetException(ex);
            StopVpnService();
            return StartCommandResult.NotSticky;
        }

        // signal start command
        if (intent?.Action == "connect")
            StartServiceTaskCompletionSource?.TrySetResult(this);

        _isServiceStarted = true;
        return StartCommandResult.Sticky;
    }

    private static void AddDnsServers(Builder builder, IPAddress[] dnsServers, bool isIpV6Supported)
    {
        if (!isIpV6Supported)
            dnsServers = dnsServers.Where(x => x.IsV4()).ToArray();

        foreach (var dnsServer in dnsServers)
            builder.AddDnsServer(dnsServer.ToString());
    }

    private static void AddAppFilter(Builder builder, string appPackageName,
        string[]? includeApps, string[]? excludeApps)
    {
        // Applications Filter
        if (includeApps != null) {
            builder.AddAllowedApplication(appPackageName);

            // make sure not to add current app to allowed apps
            foreach (var app in includeApps.Where(x => x != appPackageName))
                try {
                    builder.AddAllowedApplication(app);
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not add an allowed app. App: {app}", app);
                }
        }

        if (excludeApps != null) {
            // make sure not to add current app to disallowed apps
            foreach (var app in excludeApps.Where(x => x != appPackageName))
                try {
                    builder.AddDisallowedApplication(app);
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex, "Could not add a disallowed app. App: {app}", app);
                }
        }
    }

    private Task ReadingPacketTask()
    {
        if (_inStream == null)
            throw new ArgumentNullException(nameof(_inStream));

        try {
            var buf = new byte[short.MaxValue];
            int read;
            while (!_isClosing && (read = _inStream.Read(buf)) > 0) {
                var packetBuffer = buf[..read]; // copy buffer for packet
                var ipPacket = Packet.ParsePacket(LinkLayers.Raw, packetBuffer)?.Extract<IPPacket>();
                if (ipPacket != null)
                    ProcessPacket(ipPacket);
            }
        }
        catch (ObjectDisposedException) {
        }
        catch (Exception ex) {
            if (!VhUtil.IsSocketClosedException(ex))
                VhLogger.Instance.LogError(ex, "Error occurred in Android ReadingPacketTask.");
        }

        StopVpnService();
        return Task.FromResult(0);
    }

    public bool CanDetectInProcessPacket => OperatingSystem.IsAndroidVersionAtLeast(29);

    public bool IsInProcessPacket(ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        // check if the packet is in process
        if (!CanDetectInProcessPacket || !OperatingSystem.IsAndroidVersionAtLeast(29))
            throw new NotSupportedException("IsInProcessPacket is not supported on this device.");

        // check if the packet is from the current app
        var localAddress = new InetSocketAddress(InetAddress.GetByAddress(localEndPoint.Address.GetAddressBytes()),
            localEndPoint.Port);
        var remoteAddress = new InetSocketAddress(InetAddress.GetByAddress(remoteEndPoint.Address.GetAddressBytes()),
            remoteEndPoint.Port);

        // Android 10 and above
        var uid = _connectivityManager?.GetConnectionOwnerUid((int)protocol, localAddress, remoteAddress);
        return uid == Process.MyUid();
    }

    private PacketReceivedEventArgs? _packetReceivedEventArgs;

    protected virtual void ProcessPacket(IPPacket ipPacket)
    {
        // create the event args. for performance, we will reuse the same instance
        _packetReceivedEventArgs ??= new PacketReceivedEventArgs(new IPPacket[1], this);

        try {
            _packetReceivedEventArgs.IpPackets[0] = ipPacket;
            PacketReceivedFromInbound?.Invoke(this, _packetReceivedEventArgs);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in processing packet. Packet: {Packet}",
                VhLogger.FormatIpPacket(ipPacket.ToString()!));
        }
    }

    public override void OnDestroy()
    {
        VhLogger.Instance.LogTrace("VpnService has been destroyed!");
        CloseVpn();
        base.OnDestroy();
    }

    private bool _isClosing;

    private void CloseVpn()
    {
        if (_mInterface == null || _isClosing) return;
        _isClosing = true;

        VhLogger.Instance.LogTrace("Closing VpnService...");

        // close streams
        try {
            _inStream?.Close();
            _outStream?.Close();
            _inStream?.Dispose();
            _outStream?.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService streams.");
        }


        // close VpnService
        try {
            _mInterface?.Close(); //required to close the vpn. dispose is not enough
            _mInterface?.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService.");
        }
        finally {
            _mInterface = null;
        }

        try {
            Stopped?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while invoking Stopped event.");
        }

        _isClosing = false;
    }

    private void StopVpnService()
    {
        // make sure to close vpn; it has self check
        CloseVpn();

        // close the service
        if (!_isServiceStarted) return;
        _isServiceStarted = false;

        try {
            // it must be after _mInterface.Close
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in StopForeground of VpnService.");
        }

        try {
            StopSelf();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in StopSelf of VpnService.");
        }
    }

    void IDisposable.Dispose()
    {
        // The parent should not be disposed, never call parent dispose
        StopVpnService();
    }
}