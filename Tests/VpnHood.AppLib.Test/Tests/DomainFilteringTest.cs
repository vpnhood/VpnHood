using VpnHood.AppLib.Test.Dom;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class DomainFilteringTest : TestAppBase
{
    [TestMethod]
    public async Task IncludeDomains()
    {
        using var appDom = await AppClientServerDom.Create(TestAppHelper);
        var app = appDom.App;
        app.UserSettings.DomainFilterPolicy.Includes = [MockEps.HttpsUrl1.Host];
        app.UserSettings.DomainFilterPolicy.Excludes = [MockEps.HttpsUrl2.Host];

        // domain filter should have upper hand.
        // Here we force IpFilter to include HttpsUrl2 and exclude HttpsUrl1
        app.SettingsService.SplitByIpSettings.AppIncludes = MockEps.HttpsV4EndPoint2.Address.ToString();

        // connect
        await appDom.Connect(TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // test includes
        var oldStat = app.GetSessionStatus(cancellationToken: TestCt);
        await TestHelper.Test_Https(uri: MockEps.HttpsUrl1);
        var newStat = app.GetSessionStatus(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.TcpTunnelledCount + 1, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount, newStat.TcpPassthruCount);

        // test excludes
        oldStat = app.GetSessionStatus(cancellationToken: TestCt);
        await TestHelper.Test_Https(uri: MockEps.HttpsUrl2);
        newStat = app.GetSessionStatus(cancellationToken: TestCt);
        Assert.AreEqual(oldStat.TcpTunnelledCount, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount + 1, newStat.TcpPassthruCount);

    }
}
