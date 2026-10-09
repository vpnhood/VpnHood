using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Proxies.Management.Abstractions.Options;

namespace VpnHood.Test.Tests;

// The saved options a start without the app runs with: each reconfigure lands in them.
[TestClass]
public class VpnServiceOptionsFileTest : TestBase
{
    [TestMethod]
    public async Task A_reconfigure_lands_in_the_saved_options()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Write(new VpnServiceOptions {
            ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" }
        }, TestCt);

        // every setting off its default, so each one shows it arrived
        await serviceOptionsFile.Reconfigure(new ClientReconfigureParams {
            UseTcpProxy = true,
            DropUdp = true,
            DropQuic = true,
            AllowAnonymousTracker = false,
            ChannelProtocol = ChannelProtocol.Udp,
            UnroutedIpMode = SplitUnsupportedIpMode.Block,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude,
            ProxyOptions = new ProxyOptions { ResetStates = true, VerifyTls = false }
        }, TestCt);

        var serviceOptions = serviceOptionsFile.Read();
        var clientOptions = serviceOptions.ClientOptions;
        Assert.IsTrue(clientOptions.UseTcpProxy);
        Assert.IsTrue(clientOptions.DropUdp);
        Assert.IsTrue(clientOptions.DropQuic);
        Assert.IsFalse(clientOptions.AllowAnonymousTracker);
        Assert.AreEqual(ChannelProtocol.Udp, clientOptions.ChannelProtocol);
        Assert.AreEqual(SplitUnsupportedIpMode.Block, clientOptions.UnroutedIpMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, clientOptions.UnsupportedIpV6Mode);
        Assert.IsNotNull(serviceOptions.ProxyOptions);
        Assert.IsTrue(serviceOptions.ProxyOptions.ResetStates);
        Assert.IsFalse(serviceOptions.ProxyOptions.VerifyTls);
    }

    [TestMethod]
    public async Task Without_saved_options_a_reconfigure_makes_none()
    {
        var serviceOptionsFile = CreateServiceOptionsFile();
        await serviceOptionsFile.Reconfigure(new ClientReconfigureParams {
            UseTcpProxy = true,
            DropUdp = true,
            DropQuic = true,
            AllowAnonymousTracker = false,
            ChannelProtocol = ChannelProtocol.Udp,
            UnroutedIpMode = SplitUnsupportedIpMode.Block,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude,
            ProxyOptions = new ProxyOptions()
        }, TestCt);

        Assert.IsFalse(serviceOptionsFile.Exists);
    }

    private VpnServiceOptionsFile CreateServiceOptionsFile()
    {
        var configFolder = Path.Combine(TestHelper.WorkingPath, "VpnServiceOptionsFile_" + Guid.CreateVersion7());
        Directory.CreateDirectory(configFolder);
        return new VpnServiceOptionsFile(configFolder);
    }
}
