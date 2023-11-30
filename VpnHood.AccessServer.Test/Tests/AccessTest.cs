using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTest
{
    [TestMethod]
    public async Task Foo()
    {
        await Task.Delay(0);
        Console.Out.WriteLine(Math.Round(1200/1000d));
        Console.Out.WriteLine(Math.Round(1700 / 1000d));
        Console.Out.WriteLine((int)33.3);
    }

    [TestMethod]
    public async Task Get()
    {
        var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(true);

        var sessionDom = await accessTokenDom.CreateSession();
        await sessionDom.AddUsage(20, 10);
        await farm.TestInit.FlushCache();

        var accessDatas = await farm.TestInit.AccessesClient.ListAsync(farm.TestInit.ProjectId, accessTokenDom.AccessTokenId);
        var accessData = accessDatas.Items.Single(x => x.Access.AccessTokenId == accessTokenDom.AccessTokenId);
        Assert.AreEqual(30, accessData.Access.TotalTraffic);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);

        // check single get
        accessData = await farm.TestInit.AccessesClient.GetAsync(farm.TestInit.ProjectId, accessData.Access.AccessId);
        Assert.AreEqual(30, accessData.Access.TotalTraffic);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);
        Assert.AreEqual(accessTokenDom.AccessTokenId, accessData.Access.AccessTokenId);
        Assert.AreEqual(sessionDom.SessionRequestEx.ClientInfo.ClientId, accessData.Device?.ClientId);
    }

    [TestMethod]
    public async Task List()
    {
        var testInit2 = await TestInit.Create();
        var sample1 = await ServerFarmDom.Create(testInit2);
        var actualAccessCount = 0;
        var usageCount = 0;
        var deviceCount = 0;

        // ----------------
        // Create accessToken1 public in ServerFarm1
        // ----------------
        var sampleAccessToken1 = await sample1.CreateAccessToken(true);
        var traffic = new Traffic { Received = 1000, Sent = 500};
        
        // accessToken1 - sessions1
        actualAccessCount++;
        usageCount += 2;
        deviceCount++;
        var sessionDom = await sampleAccessToken1.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);

        // accessToken1 - sessions2
        actualAccessCount++;
        usageCount += 2;
        deviceCount++;
        sessionDom = await sampleAccessToken1.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);

        // ----------------
        // Create accessToken2 public in ServerFarm2
        // ----------------
        var sample2 = await ServerFarmDom.Create(testInit2);
        var accessToken2 = await sample2.CreateAccessToken(true);
        var sample2UsageCount = 0;
        var sample2AccessCount = 0;

        // accessToken2 - sessions1
        actualAccessCount++;
        sample2AccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        deviceCount++;
        sessionDom = await accessToken2.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);

        // accessToken2 - sessions2
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sample2AccessCount++;
        deviceCount++;
        sessionDom = await accessToken2.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);
        
        // ----------------
        // Create accessToken3 private in ServerFarm2
        // ----------------
        var accessToken3 = await sample2.CreateAccessToken();
        sample2AccessCount++;

        // accessToken3 - sessions1
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sessionDom = await accessToken3.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        usageCount += 2;
        sample2UsageCount += 2;
        sessionDom = await accessToken3.CreateSession();
        await sessionDom.AddUsage(traffic);
        await sessionDom.AddUsage(traffic);

        await testInit2.FlushCache();
        var res = await testInit2.AccessesClient.ListAsync(sample1.TestInit.ProjectId);

        Assert.IsTrue(res.Items.All(x => x.Access.LastUsedTime >= sample1.CreatedTime.AddSeconds(-1)));
        Assert.AreEqual(actualAccessCount, res.Items.Count);
        Assert.AreEqual(deviceCount, res.Items.Count(x => x.Device!=null));
        Assert.AreEqual(1, res.Items.Count(x => x.Device==null));
        Assert.AreEqual(traffic.Sent * usageCount,  res.Items.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(traffic.Received * usageCount,  res.Items.Sum(x => x.Access.CycleReceivedTraffic));

        // Check: Filter by Group
        res = await testInit2.AccessesClient.ListAsync(testInit2.ProjectId, serverFarmId: sample2.ServerFarmId);
        Assert.AreEqual(sample2AccessCount, res.Items.Count);
        Assert.AreEqual(traffic.Sent * sample2UsageCount, res.Items.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(traffic.Received * sample2UsageCount, res.Items.Sum(x => x.Access.CycleReceivedTraffic));
    }
}