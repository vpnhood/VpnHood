# Use the VPN Engine in Your Own App

You write the app, and VpnHood's engine carries its VPN: it connects to a VpnHood server and carries every packet
through the tunnel. This guide connects it two ways: in your own process, as a Windows program or service can, and
through the VPN service, as a phone app must. What the engine does inside is in
[How the VPN Engine Works](vpn-engine.md).

The engine only connects. Reconnecting when the network changes, keeping access keys, notifications and the rest of a
VPN app are yours to write, or take them all from the app's logic:
[Your Own Screens over the App's Logic](use-the-app-logic.md).

## The packages

| You run the engine | Packages |
| --- | --- |
| In your own process, on Windows | `VpnHood.Core.Client`, `VpnHood.Net.VpnAdapters.WinTun` |
| Through the VPN service, on Android | `VpnHood.Core.Client.VpnServices.Manager`, `VpnHood.Core.Client.Devices.Android`, `VpnHood.Core.Client.VpnServices.Host.Android` |

## In your own process

The client takes a network adapter, which the operating system routes the traffic into, and connects:

```csharp Snippet:EngineConnect
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
```

1. **The adapter is the platform's.** WinTun's adds a network interface, so the program runs as an administrator;
   `LinuxTunVpnAdapter` takes the same calls on Linux.
2. **The access key names the server.** `ClientOptions.SampleAccessKey` is VpnHood's sample server, which ends each
   session after a few minutes; your app connects with an access key of your own server.
3. **The client ID is the device's.** Make it once and keep it: the server counts a device by it.
4. **Disposing the client disconnects.** The session also ends on its own, when the server ends it.

## Through the VPN service

On a phone the VPN runs in a service the system starts, Android's VPN service or iOS's Network Extension, not in your
app's process. Your app starts the service, and follows its state:

```csharp Snippet:EngineVpnService
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
```

1. **On Android, the device is `AndroidDevice.Create()`,** made in your app's own process only: Android builds your
   application in the VPN service's process too, which `AndroidDevice.IsVpnServiceProcess` tells.
2. **The UI context lets the engine ask for the VPN permission.** It is your activity's, `new AndroidUiContext(activity)`,
   for an activity derived from `ActivityEvent`. The first connect brings Android's connection request.
3. **The state** is `ConnectionInfo.ClientState`: Connecting, Connected, Disposed and the steps between them.
   `StateChanged` tells you it changed, as often as the event watcher's interval.

## The samples

Each one runs these calls in a small app of its own:

- [CoreSample.WinConsole](https://github.com/vpnhood/VpnHood.App.Samples/tree/main/CoreSample.WinConsole): the
  engine in a console on Windows.
- [CoreSample.AndroidForm](https://github.com/vpnhood/VpnHood.App.Samples/tree/main/CoreSample.AndroidForm): through
  the VPN service, in a plain Android activity.
- [CoreSample.MauiForm](https://github.com/vpnhood/VpnHood.App.Samples/tree/main/CoreSample.MauiForm): through the VPN
  service, in a MAUI app on Android and Windows.

## See also

- [How the VPN Engine Works](vpn-engine.md) — how the engine takes its adapter, and how a packet travels
- [Your Own Screens over the App's Logic](use-the-app-logic.md) — the whole app under screens of your own
