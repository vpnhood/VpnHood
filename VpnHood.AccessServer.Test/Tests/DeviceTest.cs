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
        var farm1 = await ServerFarmDom.Create();
        var farm2 = await ServerFarmDom.Create();

        var accessTokenDom1 = await farm1.CreateAccessToken();
        var accessTokenDom2 = await farm2.CreateAccessToken();

        var session1 = await accessTokenDom1.CreateSession();
        var session2 = await accessTokenDom2.CreateSession();

        var device1 = await farm1.TestApp.DevicesClient.FindByClientIdAsync(farm1.TestApp.ProjectId,
            session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientId, session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientVersion, session1.SessionRequestEx.ClientInfo.ClientVersion);
        Assert.AreEqual(device1.UserAgent, session1.SessionRequestEx.ClientInfo.UserAgent);

        var device2 = await farm2.TestApp.DevicesClient.FindByClientIdAsync(farm2.TestApp.ProjectId,
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
        var clientId = Guid.NewGuid();
        await farm.DefaultServer.CreateSession(accessTokenDom.AccessToken, clientId);
        var deviceClient = farm.TestApp.DevicesClient;

        var device = await deviceClient.FindByClientIdAsync(farm.ProjectId, clientId);
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
}