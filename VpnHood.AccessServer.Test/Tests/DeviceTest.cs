using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class DeviceTest
{
    [TestMethod]
    public async Task ClientId_is_unique_per_project()
    {
        using var farm1 = await ServerFarmDom.Create();
        using var farm2 = await ServerFarmDom.Create();

        var accessTokenDom1 = await farm1.CreateAccessToken();
        var accessTokenDom2 = await farm2.CreateAccessToken();

        var session1 = await accessTokenDom1.CreateSession();
        var session2 = await accessTokenDom2.CreateSession();

        var device1 = await farm1.TestApp.DevicesClient.GetByClientIdAsync(farm1.TestApp.ProjectId,
            session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientId, session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientVersion, session1.SessionRequestEx.ClientInfo.ClientVersion);
        Assert.AreEqual(device1.UserAgent, session1.SessionRequestEx.ClientInfo.UserAgent);

        var device2 = await farm2.TestApp.DevicesClient.GetByClientIdAsync(farm2.TestApp.ProjectId,
            session2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device2.ClientId, session2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device2.ClientVersion, session2.SessionRequestEx.ClientInfo.ClientVersion);
        Assert.AreEqual(device2.UserAgent, session2.SessionRequestEx.ClientInfo.UserAgent);

        Assert.AreNotEqual(device1.DeviceId, device2.DeviceId);
    }

    [TestMethod]
    public async Task Locked()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken();
        var clientId = Guid.NewGuid().ToString();
        await farm.DefaultServer.CreateSession(accessTokenDom.AccessToken, clientId);
        var deviceClient = farm.TestApp.DevicesClient;

        var device = await deviceClient.GetByClientIdAsync(farm.ProjectId, clientId);
        Assert.IsNull(device.LockedTime);

        await deviceClient.UpdateAsync(farm.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(farm.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);

        await Task.Delay(1000);
        await deviceClient.UpdateAsync(farm.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = true } });
        device = (await deviceClient.GetAsync(farm.ProjectId, device.DeviceId)).Device;
        Assert.IsTrue(device.LockedTime > farm.CreatedTime);

        // check access
        var sessionDom =
            await farm.DefaultServer.CreateSession(accessTokenDom.AccessToken, clientId, assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionDom.SessionResponseEx.ErrorCode);

        await deviceClient.UpdateAsync(farm.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(farm.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);
        sessionDom = await farm.DefaultServer.CreateSession(accessTokenDom.AccessToken, clientId);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task List()
    {
        var sampler = await ServerFarmDom.Create();
        var sampleAccessToken = await sampler.CreateAccessToken();
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleSession1.AddUsage(10);

        var sampleSession2 = await sampleAccessToken.CreateSession();
        await sampleSession2.AddUsage(10);

        await sampler.TestApp.Sync();
        var res = await sampler.TestApp.DevicesClient.ListAsync(
            sampler.ProjectId, usageBeginTime: sampler.TestApp.CreatedTime);
        Assert.AreEqual(2, res.Count);
    }

    [TestMethod]
    public async Task Usages()
    {
        var sampler = await ServerFarmDom.Create();
        var sampleAccessToken = await sampler.CreateAccessToken();
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleSession1.AddUsage(10);

        var sampleSession2 = await sampleAccessToken.CreateSession();
        await sampleSession2.AddUsage(10);

        await sampler.TestApp.Sync();
        var res = await sampler.TestApp.DevicesClient.ListAsync(
            sampler.ProjectId, usageBeginTime: sampler.TestApp.CreatedTime);
        Assert.AreEqual(2, res.Count);
    }

    [TestMethod]
    public async Task Usages_by_access_token()
    {
        var farmDom = await ServerFarmDom.Create();
        var accessTokenDom1 = await farmDom.CreateAccessToken();
        var accessTokenDom2 = await farmDom.CreateAccessToken();
        
        var session1 = await accessTokenDom1.CreateSession();
        await session1.AddUsage(10);
        var session2 = await accessTokenDom1.CreateSession();
        await session2.AddUsage(10);

        var token2Session1 = await accessTokenDom2.CreateSession();
        await token2Session1.AddUsage(10);

        await farmDom.TestApp.Sync();
        
        var deviceDatas = await farmDom.TestApp.DevicesClient.ListUsagesAsync(farmDom.ProjectId, 
            usageBeginTime: farmDom.TestApp.CreatedTime);
        Assert.AreEqual(3, deviceDatas.Count);

        deviceDatas = await farmDom.TestApp.DevicesClient.ListUsagesAsync(farmDom.ProjectId, 
            accessTokenId: accessTokenDom1.AccessTokenId,  usageBeginTime: farmDom.TestApp.CreatedTime);
        Assert.AreEqual(2, deviceDatas.Count);
    }
}