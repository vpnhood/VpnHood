using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestVpnService
    : IVpnServiceHandler, IDisposable
{
    private readonly Func<VpnAdapterSettings, IVpnAdapter> _vpnAdapterFactory;
    private readonly NetFilter _netFilter;
    private readonly VpnServiceHost _vpnServiceHost;
    private readonly Action<VpnHoodClient>? _clientCreated;
    private readonly Func<IProxyConnector>? _proxyConnectorFactory;
    private readonly TaskCompletionSource _notificationWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _clientNotificationWaiting =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsDisposed { get; private set; }
    public IVpnAdapter? CurrentVpnAdapter { get; private set; }
    public VpnHoodClient? LastClient { get; private set; }

    // the session the host is in, where a test waits for a connect to have begun its own
    internal int SessionId => _vpnServiceHost.Context.SessionId;

    // the client the host follows, where a test looks
    internal VpnHoodClient? Client => _vpnServiceHost.Client;

    // set when a take of the tunnel starts waiting for an earlier take or release
    internal Task TunnelTakeWaiting => _vpnServiceHost.TunnelTakeWaiting;

    // set when a session's end starts waiting for a hold that another end of it is closing
    internal Task TunnelReleaseWaiting => _vpnServiceHost.TunnelReleaseWaiting;

    // set when a failed connect's end starts waiting for earlier connects or a replaced client's close
    internal Task PredecessorsWaiting => _vpnServiceHost.PredecessorsWaiting;

    // where a test holds a connect once its client is the service's and before that client starts, at the notification
    // it shows then: set, the next notification made while the service follows a client that has not started waits for it
    internal Task? ClientNotificationGate { get; set; }

    // completes once a notification waits at that gate
    internal Task ClientNotificationWaiting => _clientNotificationWaiting.Task;

    // a close of the notification that fails, as the system's refusal, where a test plays one
    internal Exception? StopNotificationException { get; set; }

    // where a test holds a connect at its first notification, the one of its Initializing, once it has read its options
    // and before its block: set, the next such notification waits for it, and others go on
    internal Task? NotificationGate { get; set; }

    // completes once a notification waits at the gate
    internal Task NotificationWaiting => _notificationWaiting.Task;

    // the reconfigure the host keeps for the next one, where a test looks
    internal ClientReconfigureParams? KeptReconfigure => _vpnServiceHost.Reconfigurer.KeptParams;

    // a reconfigure as the app's request brings it, without the options the app writes first
    internal Task Reconfigure(ClientReconfigureParams reconfigureParams)
    {
        return _vpnServiceHost.Reconfigurer.Reconfigure(reconfigureParams, CancellationToken.None);
    }

    // config folder should be read from static place in read environment, because service can be started independently
    public TestVpnService(
        string configFolder,
        NetFilter netFilter,
        Func<VpnAdapterSettings, IVpnAdapter> vpnAdapterFactory,
        IVpnTunnelLock? tunnelLock = null,
        IVpnKillSwitch? killSwitch = null,
        Action<VpnHoodClient>? clientCreated = null,
        Func<IProxyConnector>? proxyConnectorFactory = null)
    {
        _vpnAdapterFactory = vpnAdapterFactory;
        _netFilter = netFilter;
        _clientCreated = clientCreated;
        _proxyConnectorFactory = proxyConnectorFactory;
        _vpnServiceHost = new VpnServiceHost(
            configFolder,
            dataFolder: configFolder,
            this,
            socketFactory: new TestSocketFactory(),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: false,
            tunnelLock: tunnelLock,
            killSwitch: killSwitch);
    }

        // it is not async to simulate real environment
    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    public void OnStop()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    // the stop, where a test waits for its end
    public Task Disconnect()
    {
        return _vpnServiceHost.TryDisconnect();
    }

    // a connect, a forced one where a test plays the app's connect over a session under way
    public Task<bool> Connect(bool forceReconnect)
    {
        return _vpnServiceHost.TryConnect(forceReconnect);
    }

    // another app's ask for the machine's tunnel, as a tunnel lock brings it to the host, with the disconnect it begins
    internal Task Revoke()
    {
        return _vpnServiceHost.RevokeTunnel();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        CurrentVpnAdapter = _vpnAdapterFactory(adapterSettings);
        CurrentVpnAdapter.Disposed += (_, _) => CurrentVpnAdapter = null;
        return CurrentVpnAdapter;
    }

    public VpnHoodClientFactory CreateClientFactory()
    {
        return new TestClientFactory(_netFilter, client => {
            LastClient = client;
            _clientCreated?.Invoke(client);
        }, _proxyConnectorFactory);
    }

    // a test plays a host that cannot connect by itself, as Windows' VPN plug-in
    public bool CanReconnect { get; set; } = true;

    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Show notification called with connection info: {@ConnectionInfo}", connectionInfo);
        if (ClientNotificationGate is { } clientNotificationGate &&
            _vpnServiceHost.Client is { State: ClientState.None }) {
            ClientNotificationGate = null;
            _clientNotificationWaiting.TrySetResult();
            clientNotificationGate.Wait(CancellationToken.None);
            return;
        }

        if (NotificationGate is not { } notificationGate || connectionInfo.ClientState != ClientState.Initializing)
            return;

        NotificationGate = null;
        _notificationWaiting.TrySetResult();
        notificationGate.Wait(CancellationToken.None);
    }

    public void StopNotification()
    {
        VhLogger.Instance.LogInformation("Stop notification called.");
        if (StopNotificationException is { } stopNotificationException)
            throw stopNotificationException;
    }

    public void StopSelf()
    {
        VhLogger.Instance.LogInformation("Stop self called.");
        Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _vpnServiceHost.Dispose();
        VhLogger.Instance.LogInformation("TestVpnService has been disposed.");
    }
}

