using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class IpLockClientTest : ClientTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit2 = await TestInit.Create();

        var ipLockClient = testInit2.IpLocksClient;

        //-----------
        // check: Create
        //-----------
        var createParams1 = new IpLockCreateParams
        {
            IpAddress = await TestInit1.NewIpV4String(),
            IsLocked = true,
            Description = "Sigma"
        };
        await ipLockClient.CreateAsync(testInit2.ProjectId, createParams1);

        var createParams2 = new IpLockCreateParams
        {
            IpAddress = await TestInit1.NewIpV4String(),
            IsLocked = false,
            Description = "Sigma2"
        };
        await ipLockClient.CreateAsync(testInit2.ProjectId, createParams2);

        //-----------
        // check: get
        //-----------
        var ipLock = await ipLockClient.GetAsync(testInit2.ProjectId, createParams1.IpAddress);
        Assert.IsNotNull(ipLock.LockedTime);
        Assert.AreEqual(createParams1.Description, ipLock.Description);

        ipLock = await ipLockClient.GetAsync(testInit2.ProjectId, createParams2.IpAddress);
        Assert.IsNull(ipLock.LockedTime);
        Assert.AreEqual(createParams2.Description, ipLock.Description);

        //-----------
        // check: update
        //-----------
        var updateParams1 = new IpLockUpdateParams
        {
            Description = new PatchOfString { Value = Guid.NewGuid().ToString() },
            IsLocked = new PatchOfBoolean { Value = false }
        };
        await ipLockClient.UpdateAsync(testInit2.ProjectId, createParams1.IpAddress, updateParams1);
        ipLock = await ipLockClient.GetAsync(testInit2.ProjectId, createParams1.IpAddress);
        Assert.IsNull(ipLock.LockedTime);
        Assert.AreEqual(updateParams1.Description.Value, ipLock.Description);

        var updateParams2 = new IpLockUpdateParams
        {
            Description = new PatchOfString { Value = Guid.NewGuid().ToString() },
            IsLocked = new PatchOfBoolean { Value = true }
        };
        await ipLockClient.UpdateAsync(testInit2.ProjectId, createParams2.IpAddress, updateParams2);
        ipLock = await ipLockClient.GetAsync(testInit2.ProjectId, createParams2.IpAddress);
        Assert.IsNotNull(ipLock.LockedTime);
        Assert.AreEqual(updateParams2.Description.Value, ipLock.Description);

        //-----------
        // check: list
        //-----------
        var ipLocks = await ipLockClient.ListAsync(testInit2.ProjectId);
        Assert.AreEqual(2, ipLocks.Count);
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress));
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress));

        //-----------
        // check: delete
        //-----------
        await ipLockClient.DeleteAsync(testInit2.ProjectId, createParams1.IpAddress);
        ipLocks = await ipLockClient.ListAsync(testInit2.ProjectId);
        Assert.AreEqual(1, ipLocks.Count);
        Assert.IsFalse(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress));
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress));
    }

    [TestMethod]
    public async Task lock_unlock_ip()
    {
        var ipLockClient = TestInit1.IpLocksClient;
        var agentClient = TestInit1.CreateAgentClient();

        var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1, hostEndPoint: TestInit1.HostEndPointG1S1, clientIp: await TestInit1.NewIpV4Db());
        sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
        sessionRequestEx.ClientInfo.ClientVersion = "1.0.0";

        // check lock
        await ipLockClient.CreateAsync(TestInit1.ProjectId, new IpLockCreateParams { IpAddress = sessionRequestEx.ClientIp!.ToString(), IsLocked = true });
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);

        // check unlock
        await ipLockClient.UpdateAsync(TestInit1.ProjectId, sessionRequestEx.ClientIp!.ToString(), new IpLockUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        Assert.AreNotEqual(SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);
    }
}