using System.Net;
using System.Net.Sockets;
using VpnHood.AppUi.Hosting.Desktop.Linux;

namespace VpnHood.AppLib.Test.Tests;

// The Linux half of the connection check, against a real loopback connection of this process.
[TestClass]
public class LinuxLoopbackPeerCheckTest
{
    [TestMethod]
    public async Task The_far_end_is_this_process_and_not_another()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Linux only: the check reads /proc.");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = await listener.AcceptSocketAsync();

        var local = client.Client.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("no local end");
        var remote = client.Client.RemoteEndPoint as IPEndPoint ?? throw new InvalidOperationException("no remote end");
        var check = new LinuxLoopbackPeerCheck();

        Assert.IsTrue(await check.IsOwnedBy(local, remote, Environment.ProcessId, CancellationToken.None),
            "the listener is this process, which runs as this user");
        Assert.AreEqual(Environment.UserName == "root",
            await check.IsOwnedBy(local, remote, int.MaxValue - 1, CancellationToken.None),
            "another process is the service, which runs as root");
        Assert.IsFalse(await check.IsOwnedBy(local, new IPEndPoint(IPAddress.Loopback, 1), Environment.ProcessId,
            CancellationToken.None), "no such connection");
    }
}
