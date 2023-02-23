using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTest
{
    [TestMethod]
    public async Task Foo()
    {
        await Task.Delay(0);
        //var serverStatus = await testInit.VhContext.ServerStatuses.Where(x =>
        //    x.ServerId == Guid.Parse("2F407E77-D088-4B6A-836D-08CFE7674A26") && x.IsLast).SingleAsync();

        //serverStatus.IsLast = false;
        //serverStatus.SessionCount = 10;
        //await testInit.VhContext.SaveChangesAsync();


        //await tran.RollbackAsync();
        //var sample = await AccessPointGroupDom.Create();
        //var token = await sample.CreateAccessToken(false);
        //await sample.TestInit.CacheService.InvalidateSessions();

        //var sessionTasks = new List<Task<SessionDom>>();
        //for (var i = 0; i < 40; i++)
        //    sessionTasks.Add(token.CreateSession());

        //await Task.WhenAll(sessionTasks);
    }

    [TestMethod]
    public async Task Get()
    {
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken(true);

        var sessionDom = await accessTokenDom.CreateSession();
        await sessionDom.AddUsage(20, 10);
        await farm.TestInit.FlushCache();

        var accessDatas = await farm.TestInit.AccessesClient.ListAsync(farm.TestInit.ProjectId, accessTokenDom.AccessTokenId);
        var accessData = accessDatas.Single(x => x.Access.AccessTokenId == accessTokenDom.AccessTokenId);
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
        var sample1 = await AccessPointGroupDom.Create(testInit2);
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
        var sessionDom = await sampleAccessToken1.CreateSession();
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);

        // accessToken1 - sessions2
        actualAccessCount++;
        usageCount += 2;
        deviceCount++;
        sessionDom = await sampleAccessToken1.CreateSession();
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);

        // ----------------
        // Create accessToken2 public in AccessPointGroup2
        // ----------------
        var sample2 = await AccessPointGroupDom.Create(testInit2);
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
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);

        // accessToken2 - sessions2
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sample2AccessCount++;
        deviceCount++;
        sessionDom = await accessToken2.CreateSession();
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);
        
        // ----------------
        // Create accessToken3 private in AccessPointGroup2
        // ----------------
        var accessToken3 = await sample2.CreateAccessToken(false);
        sample2AccessCount++;

        // accessToken3 - sessions1
        actualAccessCount++;
        usageCount += 2;
        sample2UsageCount += 2;
        sessionDom = await accessToken3.CreateSession();
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        usageCount += 2;
        sample2UsageCount += 2;
        sessionDom = await accessToken3.CreateSession();
        await sessionDom.AddUsage(usageInfo);
        await sessionDom.AddUsage(usageInfo);

        await testInit2.FlushCache();
        var res = await testInit2.AccessesClient.ListAsync(sample1.TestInit.ProjectId);

        Assert.IsTrue(res.All(x => x.Access.LastUsedTime >= sample1.CreatedTime.AddSeconds(-1)));
        Assert.AreEqual(actualAccessCount, res.Count);
        Assert.AreEqual(deviceCount, res.Count(x => x.Device!=null));
        Assert.AreEqual(1, res.Count(x => x.Device==null));
        Assert.AreEqual(usageInfo.SentTraffic * usageCount,  res.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * usageCount,  res.Sum(x => x.Access.CycleReceivedTraffic));

        // Check: Filter by Group
        res = await testInit2.AccessesClient.ListAsync(testInit2.ProjectId, accessPointGroupId: sample2.AccessPointGroupId);
        Assert.AreEqual(sample2AccessCount, res.Count);
        Assert.AreEqual(usageInfo.SentTraffic * sample2UsageCount, res.Sum(x => x.Access.CycleSentTraffic));
        Assert.AreEqual(usageInfo.ReceivedTraffic * sample2UsageCount, res.Sum(x => x.Access.CycleReceivedTraffic));
    }
}