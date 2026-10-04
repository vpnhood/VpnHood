using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Manager;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnServiceManagerTest : TestBase
{
    [TestMethod]
    public async Task Reading_the_status_file_does_not_fail_the_services_write()
    {
        using var device = TestHelper.CreateNullDevice();
        using var manager = new VpnServiceManager(device, eventWatcherInterval: null);
        var statusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);

        // while the service disconnects, the manager learns its state from the file alone
        var statusJson = JsonSerializer.Serialize(new ConnectionInfo {
            ClientState = ClientState.Disconnecting,
            CreatedTime = DateTime.UtcNow,
            ClientStateChangedTime = null,
            ClientStateProgress = null,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            Error = null
        });

        // the manager reads as fast as it can
        using var readCts = new CancellationTokenSource();
        var readTask = Task.Run(async () => {
            while (!readCts.IsCancellationRequested)
                await manager.RefreshState(CancellationToken.None);
        }, TestCt);

        // the service writes its state as VpnServiceContext does: a write a reader kept out throws, and the
        // service would lose that state
        try {
            for (var i = 0; i < 1000; i++)
                await File.WriteAllTextAsync(statusFilePath, statusJson, TestCt);
        }
        finally {
            await readCts.CancelAsync();
            await readTask;
        }

        // a read that caught the file half-written was passed over: the manager has what was written
        await manager.RefreshState(TestCt);
        Assert.AreEqual(ClientState.Disconnecting, manager.ConnectionInfo.ClientState);
    }
}
