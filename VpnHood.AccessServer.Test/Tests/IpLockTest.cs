using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class IpLockTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testApp1 = await TestApp.Create();
        var testApp2 = await TestApp.Create();

        var ipLockClient = testApp2.IpLocksClient;

        //-----------
        // check: Create
        //-----------
        var createParams1 = new IpLockCreateParams {
            IpAddress = testApp1.NewIpV4().ToString(),
            IsLocked = true,
            Description = "Sigma"
        };
        await ipLockClient.CreateAsync(testApp2.ProjectId, createParams1);

        var createParams2 = new IpLockCreateParams {
            IpAddress = testApp1.NewIpV4().ToString(),
            IsLocked = false,
            Description = "Sigma2"
        };
        await ipLockClient.CreateAsync(testApp2.ProjectId, createParams2);

        //-----------
        // check: get
        //-----------
        var ipLock = await ipLockClient.GetAsync(testApp2.ProjectId, createParams1.IpAddress);
        Assert.IsNotNull(ipLock.LockedTime);
        Assert.AreEqual(createParams1.Description, ipLock.Description);

        ipLock = await ipLockClient.GetAsync(testApp2.ProjectId, createParams2.IpAddress);
        Assert.IsNull(ipLock.LockedTime);
        Assert.AreEqual(createParams2.Description, ipLock.Description);

        //-----------
        // check: update
        //-----------
        var updateParams1 = new IpLockUpdateParams {
            Description = new PatchOfString { Value = Guid.NewGuid().ToString() },
            IsLocked = new PatchOfBoolean { Value = false }
        };
        await ipLockClient.UpdateAsync(testApp2.ProjectId, createParams1.IpAddress, updateParams1);
        ipLock = await ipLockClient.GetAsync(testApp2.ProjectId, createParams1.IpAddress);
        Assert.IsNull(ipLock.LockedTime);
        Assert.AreEqual(updateParams1.Description.Value, ipLock.Description);

        var updateParams2 = new IpLockUpdateParams {
            Description = new PatchOfString { Value = Guid.NewGuid().ToString() },
            IsLocked = new PatchOfBoolean { Value = true }
        };
        await ipLockClient.UpdateAsync(testApp2.ProjectId, createParams2.IpAddress, updateParams2);
        ipLock = await ipLockClient.GetAsync(testApp2.ProjectId, createParams2.IpAddress);
        Assert.IsNotNull(ipLock.LockedTime);
        Assert.AreEqual(updateParams2.Description.Value, ipLock.Description);

        //-----------
        // check: list
        //-----------
        var ipLocks = await ipLockClient.ListAsync(testApp2.ProjectId);
        Assert.AreEqual(2, ipLocks.Count);
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress));
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress));

        //-----------
        // check: delete
        //-----------
        await ipLockClient.DeleteAsync(testApp2.ProjectId, createParams1.IpAddress);
        ipLocks = await ipLockClient.ListAsync(testApp2.ProjectId);
        Assert.AreEqual(1, ipLocks.Count);
        Assert.IsFalse(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress));
        Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress));
    }

    [TestMethod]
    public async Task lock_unlock_ip()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom = await farm.CreateAccessToken();

        var clientIp = farm.TestApp.NewIpV4();
        await accessTokenDom.CreateSession(clientIp: clientIp);

        // check lock
        await farm.TestApp.IpLocksClient.CreateAsync(farm.ProjectId,
            new IpLockCreateParams { IpAddress = clientIp.ToString(), IsLocked = true });
        var sessionDom = await accessTokenDom.CreateSession(clientIp: clientIp, throwError: false);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionDom.SessionResponseEx.ErrorCode);

        // check unlock
        await farm.TestApp.IpLocksClient.UpdateAsync(farm.ProjectId, clientIp.ToString(),
            new IpLockUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        sessionDom = await accessTokenDom.CreateSession(clientIp: clientIp);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
    }
}