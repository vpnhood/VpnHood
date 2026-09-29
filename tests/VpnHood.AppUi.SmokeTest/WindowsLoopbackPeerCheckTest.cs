using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.AppUi.Hosting.Cli.Windows;

namespace VpnHood.AppUi.SmokeTest;

// The Windows half of the connection check, against a real loopback connection of this process: the
// far end's owning process read from the TCP table. No elevation needed - the table is anyone's.
[TestClass]
public class WindowsLoopbackPeerCheckTest
{
    [TestMethod]
    public async Task The_far_end_is_this_process_and_not_another()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = await listener.AcceptSocketAsync();

        var local = client.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("no local end");
        var remote = client.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("no remote end");
        var check = new WindowsLoopbackPeerCheck();

        Assert.IsTrue(await check.IsOwnedBy(local, remote, Environment.ProcessId, CancellationToken.None),
            "the listener is this process");
        Assert.IsFalse(await check.IsOwnedBy(local, remote, Environment.ProcessId + 1, CancellationToken.None),
            "another process id owns nothing here");
    }
}
