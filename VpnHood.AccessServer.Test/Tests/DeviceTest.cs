using System;
using System.Net;
using System.Threading.Tasks;
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
        var farm1 = await AccessPointGroupDom.Create();
        var farm2 = await AccessPointGroupDom.Create();

        var accessTokenDom1 = await farm1.CreateAccessToken(false);
        var accessTokenDom2 = await farm2.CreateAccessToken(false);

        var session1 = await accessTokenDom1.CreateSession();
        var session2 = await accessTokenDom2.CreateSession();

        var device1 = await farm1.TestInit.DevicesClient.FindByClientIdAsync(farm1.TestInit.ProjectId, session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientId, session1.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientVersion, session1.SessionRequestEx.ClientInfo.ClientVersion);
        Assert.AreEqual(device1.UserAgent, session1.SessionRequestEx.ClientInfo.UserAgent);

        var device2 = await farm2.TestInit.DevicesClient.FindByClientIdAsync(farm2.TestInit.ProjectId, session2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device2.ClientId, session2.SessionRequestEx.ClientInfo.ClientId);
        Assert.AreEqual(device2.ClientVersion, session2.SessionRequestEx.ClientInfo.ClientVersion);
        Assert.AreEqual(device2.UserAgent, session2.SessionRequestEx.ClientInfo.UserAgent);

        Assert.AreNotEqual(device1.DeviceId, device2.DeviceId);
    }

    [TestMethod]
    public async Task Locked()
    {
        var testInit = await TestInit.Create();
        var clientId = Guid.NewGuid();
        var sessionRequestEx = testInit.CreateSessionRequestEx(testInit.AccessToken1, clientId: clientId,
            clientIp: IPAddress.Parse("1.1.1.1"));

        var agentClient = testInit.CreateAgentClient();
        await agentClient.Session_Create(sessionRequestEx);

        var deviceClient = testInit.DevicesClient;
        var device = await deviceClient.FindByClientIdAsync(testInit.ProjectId, clientId);
        Assert.IsNull(device.LockedTime);

        await deviceClient.UpdateAsync(testInit.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(testInit.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);

        await deviceClient.UpdateAsync(testInit.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = true } });
        device = (await deviceClient.GetAsync(testInit.ProjectId, device.DeviceId)).Device;
        Assert.IsTrue(device.LockedTime > testInit.CreatedTime);

        // check access
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);

        await deviceClient.UpdateAsync(testInit.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(testInit.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);
        sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task List()
    {
        var sampler = await AccessPointGroupDom.Create();
        var sampleAccessToken = await sampler.CreateAccessToken(false);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleSession1.AddUsage(10);

        var sampleSession2 = await sampleAccessToken.CreateSession();
        await sampleSession2.AddUsage(10);

        await sampler.TestInit.Sync();
        var res = await sampler.TestInit.DevicesClient.ListAsync(
            sampler.ProjectId, usageStartTime: sampler.TestInit.CreatedTime);
        Assert.AreEqual(2, res.Count);
    }

    [TestMethod]
    public async Task Usages()
    {
        var sampler = await AccessPointGroupDom.Create();
        var sampleAccessToken = await sampler.CreateAccessToken(false);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleSession1.AddUsage(10);

        var sampleSession2 = await sampleAccessToken.CreateSession();
        await sampleSession2.AddUsage(10);

        await sampler.TestInit.Sync();
        var res = await sampler.TestInit.DevicesClient.ListAsync(
            sampler.ProjectId, usageStartTime: sampler.TestInit.CreatedTime);
        Assert.AreEqual(2, res.Count);
    }

}
