using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Core.Client.Devices.Linux;

public class LinuxVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    public bool IsDisposed { get; private set; }

    public LinuxVpnService(string configFolder)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder: configFolder, 
            vpnServiceHandler: this,
            socketFactory: new MsQuicSocketFactory(),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: false);
    }

    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    public void OnDisconnect()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        var vpnAdapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
            // the app's name as an interface name the kernel takes: a debug build's is too long
            AdapterName = LinuxTunVpnAdapter.GetValidAdapterName(adapterSettings.AdapterName),
            AppId = adapterSettings.AppId,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
            QueueCapacity = adapterSettings.QueueCapacity
        });

        return vpnAdapter;
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
    }

    public void StopNotification()
    {
    }

    public void StopSelf()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _vpnServiceHost.Dispose();
    }
}