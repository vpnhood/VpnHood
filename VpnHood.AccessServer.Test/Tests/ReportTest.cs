using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ReportTest
{
    [TestMethod]
    public async Task GetUsage()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom1 = await farm.CreateAccessToken();
        var accessTokenDom2 = await farm.CreateAccessToken();

        var sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage();

        sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage();

        sessionDom = await accessTokenDom2.CreateSession();
        await sessionDom.AddUsage();

        sessionDom = await accessTokenDom2.CreateSession();
        await sessionDom.AddUsage();

        await farm.TestApp.Sync();

        var res = await farm.TestApp.ReportClient.GetUsageAsync(farm.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.AreEqual(4, res.DeviceCount);
    }

    [TestMethod]
    public async Task GetStatusHistory()
    {
        using var farm = await ServerFarmDom.Create();
        var res = await farm.TestApp.ReportClient.GetStatusHistoryAsync(farm.ProjectId, DateTime.UtcNow.AddDays(-1));
        Assert.IsTrue(res.Count > 0);
    }
}