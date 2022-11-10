using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Sampler;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class DeviceClientTest : ClientTest
{
    [TestMethod]
    public async Task ClientId_is_unique_per_project()
    {
        var testInit2 = await TestInit.Create();

        var clientId = Guid.NewGuid();
        var sessionRequestEx1 =
            TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1, clientId, clientIp: IPAddress.Parse("1.1.1.1"));
        sessionRequestEx1.ClientInfo.UserAgent = "ClientR1";

        var sessionRequestEx2 =
            testInit2.CreateSessionRequestEx(testInit2.AccessToken1, clientId, clientIp: IPAddress.Parse("1.1.1.2"));
        sessionRequestEx2.ClientInfo.UserAgent = "ClientR2";

        var agentClient1 = TestInit1.CreateAgentClient();
        var agentClient2 = testInit2.CreateAgentClient();
        await agentClient1.Session_Create(sessionRequestEx1);
        await agentClient2.Session_Create(sessionRequestEx2);

        var deviceClient1 = new DeviceClient(TestInit1.Http);

        var device1 = await deviceClient1.FindByClientIdAsync(TestInit1.ProjectId, clientId);
        Assert.AreEqual(device1.ClientId, sessionRequestEx1.ClientInfo.ClientId);
        Assert.AreEqual(device1.ClientVersion, sessionRequestEx1.ClientInfo.ClientVersion);
        Assert.AreEqual(device1.UserAgent, sessionRequestEx1.ClientInfo.UserAgent);

        var deviceClient2 = new DeviceClient(testInit2.Http);
        var device2 = await deviceClient2.FindByClientIdAsync(testInit2.ProjectId, clientId);
        Assert.AreEqual(device2.ClientId, sessionRequestEx2.ClientInfo.ClientId);
        Assert.AreEqual(device2.ClientVersion, sessionRequestEx2.ClientInfo.ClientVersion);
        Assert.AreEqual(device2.UserAgent, sessionRequestEx2.ClientInfo.UserAgent);

        Assert.AreNotEqual(device1.DeviceId, device2.DeviceId);
    }

    [TestMethod]
    public async Task Locked()
    {
        var clientId = Guid.NewGuid();
        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1, clientId: clientId,
            clientIp: IPAddress.Parse("1.1.1.1"));

        var agentClient = TestInit1.CreateAgentClient();
        await agentClient.Session_Create(sessionRequestEx);

        var deviceClient = new DeviceClient(TestInit1.Http);
        var device = await deviceClient.FindByClientIdAsync(TestInit1.ProjectId, clientId);
        Assert.IsNull(device.LockedTime);

        await deviceClient.UpdateAsync(TestInit1.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(TestInit1.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);

        await deviceClient.UpdateAsync(TestInit1.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = true } });
        device = (await deviceClient.GetAsync(TestInit1.ProjectId, device.DeviceId)).Device;
        Assert.IsTrue(device.LockedTime > TestInit1.CreatedTime);

        // check access
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);

        await deviceClient.UpdateAsync(TestInit1.ProjectId, device.DeviceId,
            new DeviceUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        device = (await deviceClient.GetAsync(TestInit1.ProjectId, device.DeviceId)).Device;
        Assert.IsNull(device.LockedTime);
        sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task List()
    {
        var sampler = await SampleAccessPointGroup.Create();
        var sampleAccessToken = await sampler.CreateAccessToken(false);
        var sampleSession1 = await sampleAccessToken.CreateSession();
        await sampleSession1.AddUsage(10);
        
        var sampleSession2 = await sampleAccessToken.CreateSession();
        await sampleSession2.AddUsage(10);

        await sampler.TestInit.Sync();
        var res = await sampler.TestInit.DeviceClient.ListAsync(
            sampler.ProjectId,  usageStartTime: sampler.TestInit.CreatedTime);
        Assert.AreEqual(2, res.Count);
    }
}