using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Sampler;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessClientTest : ClientTest
{
    [TestMethod]
    public async Task Foo()
    {
        await Task.Delay(0);
        var key = VpnHood.Common.Util.GenerateSessionKey();
        var keyString = Convert.ToBase64String(key);
        var jwt = Agent.Program.CreateSystemToken(Convert.FromBase64String(keyString), "code1");
    }

    [TestMethod]
    public async Task Get()
    {
        var sample = await SampleAccessPointGroup.Create();
        var sampleAccessToken = await sample.CreateAccessToken(true);

        var sampleSession = await sampleAccessToken.CreateSession();
        await sampleSession.AddUsage(20, 10);
        await sample.TestInit.FlushCache();

        var accessDatas = await sample.TestInit.AccessClient.ListAsync(sample.TestInit.ProjectId, sampleAccessToken.AccessTokenId);
        var accessData = accessDatas.Single(x => x.Access.AccessTokenId == sampleAccessToken.AccessTokenId);
        Assert.AreEqual(30, accessData.Access.TotalTraffic);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);

        // check single get
        accessData = await sample.TestInit.AccessClient.GetAsync(sample.TestInit.ProjectId, accessData.Access.AccessId);
        Assert.AreEqual(30, accessData.Access.TotalTraffic);
        Assert.AreEqual(30, accessData.Access.CycleTraffic);
        Assert.AreEqual(sampleAccessToken.AccessTokenId, accessData.AccessToken.AccessTokenId);
        Assert.AreEqual(sampleSession.SessionRequestEx.ClientInfo.ClientId, accessData.Device?.ClientId);
    }

    [TestMethod]
    public async Task List()
    {
        var testInit2 = await TestInit.Create();
        var sample1 = await SampleAccessPointGroup.Create(testInit2);
        var actualAccessCount = 0;
        var usageCount = 0;
        var deviceCount = 0;

        // ----------------
        // Create accessToken1 public in AccessPointGroup1
        // ----------------
        var sampleAccessToken1 = await sample1.CreateAccessToken(true);
        var usageInfo = new UsageInfo { ReceivedTraffic = 1000, SentTraffic = 500};
        
        // accessToken1 - sessions1
        actualAccessCount++;
        usageCount += 2;
        deviceCount++;
        var sampleSession = await sampleAccessToken1.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);

        // accessToken1 - sessions2
        actualAccessCount++;
        usageCount += 2;
        deviceCount++;
        sampleSession = await sampleAccessToken1.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);

        // ----------------
        // Create accessToken2 public in AccessPointGroup2
        // ----------------
        var sample2 = await SampleAccessPointGroup.Create(testInit2);
        var accessToken2 = await sample2.CreateAccessToken(true);
        var sample2UsageCount = 0;
        var sample2AccessCount = 0;

        // accessToken2 - sessions1
        actualAccessCount++;
        sample2AccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        deviceCount++;
        sampleSession = await accessToken2.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);

        // accessToken2 - sessions2
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sample2AccessCount++;
        deviceCount++;
        sampleSession = await accessToken2.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);
        
        // ----------------
        // Create accessToken3 private in AccessPointGroup2
        // ----------------
        var accessToken3 = await sample2.CreateAccessToken(false);
        sample2AccessCount++;

        // accessToken3 - sessions1
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sampleSession = await accessToken3.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        usageCount += 2;
        sample2UsageCount += 2;
        sampleSession = await accessToken3.CreateSession();
        await sampleSession.AddUsage(usageInfo);
        await sampleSession.AddUsage(usageInfo);

        await testInit2.FlushCache();
        var res = await testInit2.AccessClient.ListAsync(sample1.TestInit.ProjectId);

        Assert.IsTrue(res.All(x => x.Access.AccessedTime >= sample1.CreatedTime.AddSeconds(-1)));
        Assert.AreEqual(actualAccessCount, res.Count);
        Assert.AreEqual(deviceCount, res.Count(x => x.Device!=null));
        Assert.AreEqual(1, res.Count(x => x.Device==null));
        Assert.AreEqual(usageInfo.SentTraffic * usageCount,  res.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * usageCount,  res.Sum(x => x.Access.CycleReceivedTraffic));

        // Check: Filter by Group
        res = await testInit2.AccessClient.ListAsync(testInit2.ProjectId, accessPointGroupId: sample2.AccessPointGroupId);
        Assert.AreEqual(sample2AccessCount, res.Count);
        Assert.AreEqual(usageInfo.SentTraffic * sample2UsageCount, res.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * sample2UsageCount, res.Sum(x => x.Access.CycleReceivedTraffic));
    }
}