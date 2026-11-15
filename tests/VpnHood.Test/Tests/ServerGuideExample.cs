using System.Net;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;

namespace VpnHood.Test.Tests;

// docs/run-a-server.md, the guide "Run a VPN server from your own code", is a copy of the Snippet region here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit the region, then run the script.
// Compiled with the tests and never run: it listens on port 443 of every address and finds the machine's public
// address on the internet. The tests' own servers (TestHelper.CreateServer) run the same calls on free local ports.
internal static class ServerGuideExample
{
    internal static async Task Run(string dataFolder, CancellationToken cancellationToken)
    {
        #region Snippet:ServerRun
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
        #endregion
    }
}
