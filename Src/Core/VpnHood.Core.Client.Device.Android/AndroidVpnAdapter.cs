using System.Net;
using Android.Net;
using Android.OS;
using Java.IO;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.Device.Droid;

public class AndroidVpnAdapter(VpnService vpnService) : IVpnAdapter
{
    private FileInputStream? _inStream; // Packets to be sent are queued in this input stream.
    private ParcelFileDescriptor? _mInterface;
    private FileOutputStream? _outStream; // Packets received need to be written to this output stream.
    private PacketReceivedEventArgs? _packetReceivedEventArgs;
    private bool _stopRequested;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;
    public bool Started => _mInterface != null;
    public bool CanSendPacketToOutbound => false;
    public bool IsDnsServersSupported => true;
    public bool CanProtectSocket => true;

    protected void ProcessPacket(IPPacket ipPacket)
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

    public void StartCapture(VpnAdapterOptions options)
    {
        if (Started)
            StopCapture();

        // reset the stop request
        _stopRequested = false;

        VhLogger.Instance.LogDebug("Starting the adapter...");
        
        var builder = new VpnService.Builder(vpnService)
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
        var appPackageName = vpnService.ApplicationContext?.PackageName ?? throw new Exception("Could not get the app PackageName!");
        AddAppFilter(builder, includeApps: options.IncludeApps, excludeApps: options.ExcludeApps,
            appPackageName: appPackageName);

        // DNS Servers
        AddDnsServers(builder, options.DnsServers, options.IncludeNetworks.Any(x => x.IsIpV6));

        // try to establish the connection
        _mInterface = builder.Establish() ?? throw new Exception("Could not establish VpnService.");

        //Packets to be sent are queued in this input stream.
        _inStream = new FileInputStream(_mInterface.FileDescriptor);

        //Packets received need to be written to this output stream.
        _outStream = new FileOutputStream(_mInterface.FileDescriptor);

        Task.Run(ReadingPacketTask);
    }

    private readonly Lock _stopCaptureLock = new();
    public void StopCapture()
    {
        using var lockScope = _stopCaptureLock.EnterScope();

        if (_mInterface == null || _stopRequested) return;
        _stopRequested = true;

        VhLogger.Instance.LogDebug("Stopping the adapter...");

        // close in streams
        try {
            _inStream?.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error while closing the VpnService streams.");
        }

        // close out streams
        try {
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

        _mInterface = null;
    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
        _outStream?.Write(ipPacket.Bytes);
    }

    public void SendPacketToInbound(IList<IPPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            SendPacketToInbound(ipPackets[i]);
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

    public void ProtectSocket(System.Net.Sockets.Socket socket)
    {
        if (!vpnService.Protect(socket.Handle.ToInt32()))
            throw new Exception("Could not protect socket!");
    }

    private static void AddDnsServers(VpnService.Builder builder, IPAddress[] dnsServers, bool isIpV6Supported)
    {
        if (!isIpV6Supported)
            dnsServers = dnsServers.Where(x => x.IsV4()).ToArray();

        foreach (var dnsServer in dnsServers)
            builder.AddDnsServer(dnsServer.ToString());
    }

    private static void AddAppFilter(VpnService.Builder builder, string appPackageName,
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
            while (!_stopRequested && (read = _inStream.Read(buf)) > 0) {
                var packetBuffer = buf[..read]; // copy buffer for packet
                var ipPacket = Packet.ParsePacket(LinkLayers.Raw, packetBuffer)?.Extract<IPPacket>();
                if (ipPacket != null)
                    ProcessPacket(ipPacket);
            }
        }
        catch (ObjectDisposedException) {
        }
        catch (Exception ex) {
            if (!VhUtils.IsSocketClosedException(ex))
                VhLogger.Instance.LogError(ex, "Error occurred in Android ReadingPacketTask.");
        }

        // if not requested to stop, dispose the adapter which means critical error
        if (!_stopRequested)
            Dispose();

        return Task.CompletedTask;
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopCapture();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}