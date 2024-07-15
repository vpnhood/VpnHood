using System.Net;
using System.Net.Sockets;
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
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using ProtocolType = PacketDotNet.ProtocolType;
using Socket = System.Net.Sockets.Socket;

namespace VpnHood.Client.Device.Droid;


[Service(
    Permission = Manifest.Permission.BindVpnService,
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
public class AndroidPacketCapture : VpnService, IPacketCapture
{
    public const string VpnServiceName = "VhSession";
    private IPAddress[]? _dnsServers;
    private FileInputStream? _inStream; // Packets to be sent are queued in this input stream.
    private ParcelFileDescriptor? _mInterface;
    private int _mtu;
    private FileOutputStream? _outStream; // Packets received need to be written to this output stream.
    private readonly ConnectivityManager? _connectivityManager = ConnectivityManager.FromContext(Application.Context);
    internal static TaskCompletionSource<AndroidPacketCapture>? StartServiceTaskCompletionSource { get; set; }

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public bool Started => _mInterface != null;
    private bool _isServiceStarted;
    public IpNetwork[]? IncludeNetworks { get; set; }
    public bool CanSendPacketToOutbound => false;
    public bool CanExcludeApps => true;
    public bool CanIncludeApps => true;
    public string[]? ExcludeApps { get; set; }
    public string[]? IncludeApps { get; set; }
    public bool IsMtuSupported => true;

    public int Mtu
    {
        get => _mtu;
        set
        {
            if (Started)
                throw new InvalidOperationException("Could not set MTU while PacketCapture is started.");
            _mtu = value;
        }
    }

    public bool IsAddIpV6AddressSupported => true;
    public bool AddIpV6Address { get; set; }

    public bool IsDnsServersSupported => true;

    public IPAddress[]? DnsServers
    {
        get => _dnsServers;
        set
        {
            if (Started)
                throw new InvalidOperationException(
                    $"Could not set {nameof(DnsServers)} while {nameof(IPacketCapture)} is started.");

            _dnsServers = value;
        }
    }

    public void StartCapture()
    {
        var builder = new Builder(this)
            .SetBlocking(true)
            .SetSession(VpnServiceName)
            .AddAddress("192.168.199.188", 24);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            builder.SetMetered(false);

        if (AddIpV6Address)
            builder.AddAddress("fd00::1000", 64);

        // MTU
        if (Mtu != 0)
            builder.SetMtu(Mtu);

        // DNS Servers
        AddDnsServers(builder);

        // Routes
        AddRoutes(builder);

        // AppFilter
        AddAppFilter(builder);

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
        for (var i = 0; i < ipPackets.Count; i++)
        {
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
        try
        {
            AndroidDevice.Instance.OnServiceStartCommand(this, intent);
        }
        catch (Exception ex)
        {
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

    private void AddRoutes(Builder builder)
    {
        var includeNetworks = IncludeNetworks ?? IpNetwork.All;
        foreach (var network in includeNetworks)
            builder.AddRoute(network.Prefix.ToString(), network.PrefixLength);
    }

    private void AddDnsServers(Builder builder)
    {
        var dnsServers = VhUtil.IsNullOrEmpty(DnsServers) ? IPAddressUtil.GoogleDnsServers : DnsServers;
        if (!AddIpV6Address)
            dnsServers = dnsServers.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();

        foreach (var dnsServer in dnsServers)
            builder.AddDnsServer(dnsServer.ToString());
    }

    private void AddAppFilter(Builder builder)
    {
        // Applications Filter
        if (IncludeApps != null)
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
                    VhLogger.Instance.LogError(ex, "Could not add an allowed app. App: {app}", app);
                }
        }

        if (ExcludeApps != null)
        {
            var packageName = ApplicationContext?.PackageName ??
                              throw new Exception("Could not get the app PackageName!");
            foreach (var app in ExcludeApps.Where(x => x != packageName))
                try
                {
                    builder.AddDisallowedApplication(app);
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(ex, "Could not add a disallowed app. App: {app}", app);
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
            int read;
            while (!_isClosing && (read = _inStream.Read(buf)) > 0)
            {
                var packetBuffer = buf[..read]; // copy buffer for packet
                var ipPacket = Packet.ParsePacket(LinkLayers.Raw, packetBuffer)?.Extract<IPPacket>();
                if (ipPacket != null)
                    ProcessPacket(ipPacket);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (!VhUtil.IsSocketClosedException(ex))
                VhLogger.Instance.LogError(ex, "Error occurred in Android ReadingPacketTask.");
        }

        StopVpnService();
        return Task.FromResult(0);
    }

    public bool? IsInProcessPacket(ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        var localAddress = new InetSocketAddress(InetAddress.GetByAddress(localEndPoint.Address.GetAddressBytes()), localEndPoint.Port);
        var remoteAddress = new InetSocketAddress(InetAddress.GetByAddress(remoteEndPoint.Address.GetAddressBytes()), remoteEndPoint.Port);

        // Android 9 and below
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return false; //not supported

        // Android 10 and above
        var uid = _connectivityManager?.GetConnectionOwnerUid((int)protocol, localAddress, remoteAddress);
        return uid == Process.MyUid();
    }

    protected virtual void ProcessPacket(IPPacket ipPacket)
    {
        try
        {
            PacketReceivedFromInbound?.Invoke(this,
                new PacketReceivedEventArgs([ipPacket], this));
        }
        catch (Exception ex)
        {
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
        try
        {
            _inStream?.Dispose();
            _outStream?.Dispose();

        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService streams.");
        }

        // close VpnService
        try
        {
            _mInterface?.Close(); //required to close the vpn. dispose is not enough
            _mInterface?.Dispose();

        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService.");
        }
        finally
        {
            _mInterface = null;
        }

        try
        {
            Stopped?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {

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

        try
        {
            // it must be after _mInterface.Close
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Error in StopForeground of VpnService.");
        }

        try
        {
            StopSelf();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Error in StopSelf of VpnService.");
        }
    }

    void IDisposable.Dispose()
    {
        // The parent should not be disposed, never call parent dispose
        StopVpnService();
    }
}
