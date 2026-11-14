using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WinDivert;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsTun;

/// <summary>
/// Represents the VPN service on Windows: the platform's part of its host (<see cref="IVpnServiceHandler"/>), with
/// WinTun's adapter, MsQuic, a loopback TCP channel to the app, the machine's tunnel lock and the kill switch.
/// </summary>
internal class WindowsVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    /// <summary>Gets a value indicating whether the service's end has begun.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets a task that completes when the service ends, with which the VPN host's process ends.</summary>
    public Task Stopped => _stopped.Task;

    /// <summary>Initializes a new instance of the <see cref="WindowsVpnService"/> class, listening to the app at once.</summary>
    /// <param name="configFolder">The VPN service's folder, its config and data folder alike.</param>
    /// <param name="killSwitch">The kill switch's block.</param>
    /// <param name="withLogger">
    /// <see langword="true"/> to keep a log file of its own (<c>vpn.log</c>), where it runs in a process of its own, as
    /// the VPN host.
    /// </param>
    /// <exception cref="System.Net.Sockets.SocketException">It could not listen for the app.</exception>
    /// <exception cref="IOException">It could not publish its channel's file.</exception>
    public WindowsVpnService(
        string configFolder,
        IVpnKillSwitch killSwitch,
        bool withLogger)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder,
            dataFolder: configFolder,
            vpnServiceHandler: this,
            socketFactory: new ClientSocketFactory(new SystemSocketFactory(), new MsQuicClientFactory()),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: withLogger,
            tunnelLock: new WindowsVpnTunnelLock(),
            killSwitch: killSwitch);
    }

    /// <summary>Starts a connect with the saved options, in the background.</summary>
    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    /// <summary>Starts a disconnect, in the background.</summary>
    public void OnDisconnect()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    /// <inheritdoc/>
    /// <remarks>It is the default one.</remarks>
    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    /// <inheritdoc/>
    public bool CanReconnect => true;

    /// <inheritdoc/>
    /// <remarks>
    /// It is WinTun's, or WinDivert's where the debug switches have <c>/windivert</c>, which leaves the local networks
    /// out but for the DNS queries to them, as it simulates the DNS servers.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The settings' queue capacity is less than 1.</exception>
    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        IVpnAdapter vpnAdapter = debugData?.Contains("/windivert", StringComparison.OrdinalIgnoreCase) is true
            ? new WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
                AdapterName = adapterSettings.AdapterName,
                AutoRestart = adapterSettings.AutoRestart,
                MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
                Blocking = adapterSettings.Blocking,
                QueueCapacity = adapterSettings.QueueCapacity,
                ExcludeLocalNetwork = true
            })
            : new WinTunVpnAdapter(new WinVpnAdapterSettings {
                AdapterName = adapterSettings.AdapterName,
                AutoRestart = adapterSettings.AutoRestart,
                MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
                Blocking = adapterSettings.Blocking,
                AutoMetric = adapterSettings.AutoMetric,
                QueueCapacity = adapterSettings.QueueCapacity
            });

        return vpnAdapter;
    }

    /// <inheritdoc/>
    /// <remarks>Windows shows none here: it does nothing.</remarks>
    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Windows shows none here: it does nothing.</remarks>
    public void StopNotification()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It disposes the service, which completes <see cref="Stopped"/>.</remarks>
    public void StopSelf()
    {
        Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>It ends the service at once, its host with it, and completes <see cref="Stopped"/>.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _vpnServiceHost.Dispose();
        _stopped.TrySetResult();
    }
}