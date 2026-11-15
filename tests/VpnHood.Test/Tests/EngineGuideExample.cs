using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Test.Tests;

// docs/use-the-engine.md, the guide "Use the VPN engine in your own app", is a copy of the Snippet regions here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
// Compiled with the tests and never run: the first takes the machine's traffic through WinTun, which needs an
// administrator, and the second needs a platform's device and its VPN permission. VpnHood.App.Samples' CoreSample.*
// run the same calls.
internal static class EngineGuideExample
{
    internal static async Task ConnectInProcess(CancellationToken cancellationToken)
    {
        #region Snippet:EngineConnect
        // the network interface the tunnel's packets go through; WinTun's needs an administrator
        using var adapter = new WinTunVpnAdapter(new WinTunVpnAdapterSettings {
            AdapterName = "MyVpn",
            Blocking = true
        });

        var options = new ClientOptions {
            AdapterName = "MyVpn",
            ClientId = Guid.NewGuid().ToString(), // the device's: make it once, then keep it
            // VpnHood's sample server, for tests: your app uses your own server's access key
            AccessKey = ClientOptions.SampleAccessKey
        };

        var workingFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyVpn");
        await using var client = new VpnHoodClient(
            adapter, new ClientSocketFactory(new SystemSocketFactory(), quicClientFactory: null),
            netFilter: null, proxyConnector: null, tracker: null,
            workingFolder: workingFolder, options: options);

        await client.Connect(cancellationToken);

        // connected: the traffic goes through the tunnel until the session ends,
        // or the client is disposed
        while (client.State != ClientState.Disposed)
            await Task.Delay(1000, cancellationToken);
        #endregion
    }

    internal static async Task ConnectThroughVpnService(IDevice device, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        #region Snippet:EngineVpnService
        // the app's side of the VPN service; on Android, the device is AndroidDevice.Create()
        var serviceManager = new VpnServiceManager(device, eventWatcherInterval: TimeSpan.FromSeconds(1));
        serviceManager.StateChanged += (_, _) =>
            Console.WriteLine($"State: {serviceManager.ConnectionInfo.ClientState}");

        // starts the VPN service, which runs the engine; the UI context lets it ask
        // for the VPN permission
        await serviceManager.Start(uiContext, new VpnServiceOptions {
            ClientOptions = new ClientOptions {
                AdapterName = "MyVpn",
                ClientId = Guid.NewGuid().ToString(), // the device's: make it once, then keep it
                // VpnHood's sample server, for tests: your app uses your own server's access key
                AccessKey = ClientOptions.SampleAccessKey
            }
        }, cancellationToken);

        // to disconnect
        await serviceManager.TryStop();
        #endregion
    }
}
