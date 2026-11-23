using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;

namespace VpnHood.Test.Tests;

// The saved options a start without the app runs with: the app makes them again at each change, and what
// only a connect knows stays.
[TestClass]
public class VpnServiceOptionsFileTest : TestBase
{
    [TestMethod]
    public async Task An_update_keeps_what_only_a_connect_knows()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Write(new VpnServiceOptions {
            ClientOptions = new ClientOptions {
                AdapterName = "test", AccessKey = "test", ClientId = "test",
                PlanId = ConnectPlanId.PremiumByTrial, IsAlwaysOn = true
            }
        }, TestCt);

        // made again from the settings, which know neither the plan nor always-on
        await serviceOptionsFile.Update(new VpnServiceOptions {
            ClientOptions = new ClientOptions {
                AdapterName = "test", AccessKey = "another", ClientId = "test", SplitLocalNetwork = true
            }
        }, TestCt);

        var savedOptions = await serviceOptionsFile.Read(TestCt);
        var clientOptions = savedOptions.ClientOptions;
        Assert.AreEqual("another", clientOptions.AccessKey);
        Assert.IsTrue(clientOptions.SplitLocalNetwork);
        Assert.AreEqual(ConnectPlanId.PremiumByTrial, clientOptions.PlanId);
        Assert.IsTrue(clientOptions.IsAlwaysOn);
    }

    [TestMethod]
    public async Task Without_saved_options_an_update_makes_none()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Update(new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" }
        }, TestCt);

        Assert.IsFalse(serviceOptionsFile.Exists);
    }

    // the VPN service reads them at its start, in a process of its own, while the app may be writing them: each
    // waits out the other's moment on the file
    [TestMethod]
    public async Task A_read_and_an_update_wait_out_the_other_process_on_the_file()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Write(new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" }
        }, TestCt);

        var holder = new FileStream(serviceOptionsFile.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var readTask = Task.Run(() => serviceOptionsFile.Read(TestCt), TestCt);
        await Task.Delay(150, TestCt);
        await holder.DisposeAsync();
        Assert.AreEqual("test", (await readTask).ClientOptions.AccessKey);

        holder = new FileStream(serviceOptionsFile.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var updateTask = Task.Run(() => serviceOptionsFile.Update(new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "another", ClientId = "test" }
        }, TestCt), TestCt);
        await Task.Delay(150, TestCt);
        await holder.DisposeAsync();
        await updateTask;
        var savedOptions = await serviceOptionsFile.Read(TestCt);
        Assert.AreEqual("another", savedOptions.ClientOptions.AccessKey);
    }

    // a cancel ends the wait for the other process at once, rather than at the timeout
    [TestMethod]
    public async Task A_cancel_ends_the_wait_for_the_other_process()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Write(new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" }
        }, TestCt);

        await using var holder = new FileStream(serviceOptionsFile.FilePath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var readTask = Task.Run(() => serviceOptionsFile.Read(cancellationTokenSource.Token), TestCt);
        await Assert.ThrowsAsync<OperationCanceledException>(() => readTask);
    }

    // A proxies' reset is a word to a live connector, which the reconfigure carries; saved, it cleared the proxies'
    // ratings again at every start without the app, as the tile's or always-on's. It is not saved
    [TestMethod]
    public async Task A_proxies_reset_is_not_saved()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Write(CreateServiceOptions(resetStates: true), TestCt);
        var savedOptions = await serviceOptionsFile.Read(TestCt);
        Assert.AreEqual(ProxyMode.Managed, savedOptions.ProxyOptions?.Mode);
        Assert.AreEqual(false, savedOptions.ProxyOptions?.ResetStates);

        await serviceOptionsFile.Update(CreateServiceOptions(resetStates: true), TestCt);
        savedOptions = await serviceOptionsFile.Read(TestCt);
        Assert.AreEqual(false, savedOptions.ProxyOptions?.ResetStates);
    }

    // A file an older app saved holds the reset it was sent with, and each start without the app cleared the proxies'
    // ratings again until the app saved the options anew. A saved reset is read as none
    [TestMethod]
    public async Task A_proxies_reset_an_older_app_saved_is_read_as_none()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await File.WriteAllTextAsync(serviceOptionsFile.FilePath,
            JsonSerializer.Serialize(CreateServiceOptions(resetStates: true)), TestCt);

        var savedOptions = await serviceOptionsFile.Read(TestCt);
        Assert.AreEqual(ProxyMode.Managed, savedOptions.ProxyOptions?.Mode);
        Assert.AreEqual(false, savedOptions.ProxyOptions?.ResetStates);
    }

    private static VpnServiceOptions CreateServiceOptions(bool resetStates)
    {
        return new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" },
            ProxyOptions = new ProxyOptions { Mode = ProxyMode.Managed, ResetStates = resetStates }
        };
    }

    private VpnServiceOptionsFile CreateServiceOptionsFile()
    {
        var configFolder = Path.Combine(TestHelper.WorkingPath, "VpnServiceOptionsFile_" + Guid.CreateVersion7());
        Directory.CreateDirectory(configFolder);
        return new VpnServiceOptionsFile(configFolder);
    }
}
