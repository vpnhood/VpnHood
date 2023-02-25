using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class IpLockTest : BaseTest
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
        var farm = await AccessPointGroupDom.Create();
        var accessTokenDom = await farm.CreateAccessToken();

        var clientIp = await farm.TestInit.NewIpV4();
        await accessTokenDom.CreateSession(clientIp: clientIp);

        // check lock
        await farm.TestInit.IpLocksClient.CreateAsync(farm.ProjectId, new IpLockCreateParams { IpAddress = clientIp.ToString(), IsLocked = true });
        var sessionDom = await accessTokenDom.CreateSession(clientIp: clientIp, assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionDom.SessionResponseEx.ErrorCode);

        // check unlock
        await farm.TestInit.IpLocksClient.UpdateAsync(farm.ProjectId, clientIp.ToString(), 
            new IpLockUpdateParams { IsLocked = new PatchOfBoolean { Value = false } });
        sessionDom = await accessTokenDom.CreateSession(clientIp: clientIp);
        Assert.AreEqual(SessionErrorCode.Ok, sessionDom.SessionResponseEx.ErrorCode);
    }
}