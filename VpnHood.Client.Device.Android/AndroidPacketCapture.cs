using System.Net;
using System.Net.Sockets;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Java.IO;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

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

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public bool Started => _mInterface != null;
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
        AddVpnServers(builder);

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
        if (!Started)
            return;

        VhLogger.Instance.LogTrace("Stopping VPN Service...");
        CloseVpn();
    }

    void IDisposable.Dispose()
    {
        // The parent should not be disposed, never call parent dispose
        CloseVpn();
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent, [GeneratedEnum] StartCommandFlags flags,
        int startId)
    {
        AndroidDevice.Instance.OnServiceStartCommand(this, intent);
        return StartCommandResult.Sticky;
    }

    private void AddRoutes(Builder builder)
    {
        var includeNetworks = IncludeNetworks ?? IpNetwork.All;
        foreach (var network in includeNetworks)
            builder.AddRoute(network.Prefix.ToString(), network.PrefixLength);
    }

    private void AddVpnServers(Builder builder)
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
                    VhLogger.Instance.LogError(ex, "Could not add an allowed app. App: {app}", app);
                }
        }

        if (ExcludeApps?.Length > 0)
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
            while ((read = _inStream.Read(buf)) > 0)
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

        if (Started)
            CloseVpn();

        return Task.FromResult(0);
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

        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void CloseVpn()
    {
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

        StopVpnService();
    }

    private void StopVpnService()
    {
        try
        {
            // it must be after _mInterface.Close
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);

            StopSelf();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Error while stopping the VpnService.");
        }
    }
}
