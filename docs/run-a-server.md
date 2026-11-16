# Run a VPN Server from Your Own Code

The server engine in a .NET program of your own: it listens for clients, admits each by its access key, and carries
its traffic. Who may connect is its access manager's call; this guide keeps the access keys in files, with the file
access manager.

> [!TIP]
> Most people don't need to build a server: the VpnHood server app is ready to install, and it is the recommended way
> to run one. [Install the VpnHood server](https://www.vpnhood.com/vpnhood-server/docs/install/).

## The packages

| Package | What it is |
| --- | --- |
| `VpnHood.Core.Server` | The server engine |
| `VpnHood.Core.Server.Access.Managers.FileAccessManagers` | An access manager that keeps the access keys in files |

## Run it

```csharp Snippet:ServerRun
// who may connect: access keys kept in files, in a folder of the server's
var accessManager = await FileAccessManager.Create(
    Path.Combine(dataFolder, "access"),
    new FileAccessManagerOptions {
        // the address clients reach the server at; it listens on TCP port 443 by default
        PublicEndPoints = [IPEndPoint.Parse("203.0.113.10:443")]
    }, cancellationToken);

// an access key for one device; give it to the person who connects with it
var accessToken = await accessManager.AccessTokenService.Create(cancellationToken,
    maxClientCount: 1, tokenName: "My first key");
Console.WriteLine(accessManager.GetToken(accessToken).ToAccessKey());

// the server, which disposes the access manager with itself
await using var server = new VpnHoodServer(accessManager, new ServerOptions {
    StoragePath = dataFolder
});
await server.Start(cancellationToken);

// it serves until it is disposed
await Task.Delay(Timeout.Infinite, cancellationToken);
```

1. **The public endpoint is where clients reach the server,** and every access key carries it: the machine's public
   address, or the one a firewall or NAT forwards to it. `203.0.113.10` is an example address.
2. **An access key is what a client connects with:** `ClientOptions.AccessKey` in
   [Use the VPN Engine in Your Own App](use-the-engine.md), or one of the app's `AccessKeys`. `maxClientCount` is
   how many devices may use it at once, 0 for no limit.
3. **The server listens on TCP port 443** by default. Its UDP channel and QUIC are off until the options name their
   endpoints, `UdpEndPoints` and `QuicEndPoints`; QUIC also takes a QUIC server, `ServerOptions.QuicServer`. Open
   those ports in the firewall.
4. **A TUN adapter is optional.** Without one, the server carries the clients' traffic over connections of its own;
   with one, `ServerOptions.VpnAdapter` (`LinuxTunVpnAdapter` on Linux, which needs root), it also carries whole IP
   packets through its NAT, as [How the VPN Engine Works](vpn-engine.md#how-a-packet-travels) shows.
5. **Your own rules for who connects,** such as your accounts or billing, take an access manager of your own:
   implement `IAccessManager` and hand it to the server instead of the file access manager.

## The sample

[CoreSample.Server](https://github.com/vpnhood/VpnHood.App.Samples/tree/main/CoreSample.Server) runs this server in a
console app: it takes the public address and port as its argument, keeps its access key between runs, and with `--tun`
starts the platform's TUN adapter for the server.

## See also

- [Use the VPN Engine in Your Own App](use-the-engine.md) — the client that connects to it
- [How the VPN Engine Works](vpn-engine.md) — what happens to a packet, on the client and on the server
