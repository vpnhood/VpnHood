using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class IpLockControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Crud()
        {
            var ipLockController = TestInit2.CreateIpLockController();

            //-----------
            // check: Create
            //-----------
            var createParams1 = new IpLockCreateParams(await TestInit.NewIpV4())
            {
                IsLocked = true,
                Description = "Zigma"
            };
            await ipLockController.Create(TestInit2.ProjectId, createParams1);

            var createParams2 = new IpLockCreateParams(await TestInit.NewIpV4())
            {
                IsLocked = false,
                Description = "Zigma2"
            };
            await ipLockController.Create(TestInit2.ProjectId, createParams2);

            //-----------
            // check: get
            //-----------
            var ipLock = await ipLockController.Get(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            Assert.IsNotNull(ipLock.LockedTime);
            Assert.AreEqual(createParams1.Description, ipLock.Description);

            ipLock = await ipLockController.Get(TestInit2.ProjectId, createParams2.IpAddress.ToString());
            Assert.IsNull(ipLock.LockedTime);
            Assert.AreEqual(createParams2.Description, ipLock.Description);

            //-----------
            // check: update
            //-----------
            var updateParams1 = new IpLockUpdateParams { Description = Guid.NewGuid().ToString(), IsLocked = false };
            await ipLockController.Update(TestInit2.ProjectId, createParams1.IpAddress.ToString(), updateParams1);
            ipLock = await ipLockController.Get(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            Assert.IsNull(ipLock.LockedTime);
            Assert.AreEqual(updateParams1.Description, ipLock.Description);

            var updateParams2 = new IpLockUpdateParams { Description = Guid.NewGuid().ToString(), IsLocked = true };
            await ipLockController.Update(TestInit2.ProjectId, createParams2.IpAddress.ToString(), updateParams2);
            ipLock = await ipLockController.Get(TestInit2.ProjectId, createParams2.IpAddress.ToString());
            Assert.IsNotNull(ipLock.LockedTime);
            Assert.AreEqual(updateParams2.Description, ipLock.Description);

            //-----------
            // check: list
            //-----------
            var ipLocks = await ipLockController.List(TestInit2.ProjectId);
            Assert.AreEqual(2, ipLocks.Length);
            Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress.ToString()));
            Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress.ToString()));

            //-----------
            // check: delete
            //-----------
            await ipLockController.Delete(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            ipLocks = await ipLockController.List(TestInit2.ProjectId);
            Assert.AreEqual(1, ipLocks.Length);
            Assert.IsFalse(ipLocks.Any(x => x.IpAddress == createParams1.IpAddress.ToString()));
            Assert.IsTrue(ipLocks.Any(x => x.IpAddress == createParams2.IpAddress.ToString()));
        }

        [TestMethod]
        public async Task lock_unlock_ip()
        {
            var ipLockController = TestInit1.CreateIpLockController();
            var agentController = TestInit1.CreateAgentController();

            var sessionRequestEx = TestInit1.CreateSessionRequestEx(TestInit1.AccessToken1, hostEndPoint: TestInit1.HostEndPointG1S1, clientIp: await TestInit.NewIpV4Db());
            sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
            sessionRequestEx.ClientInfo.ClientVersion = "1.0.0";

            // check lock
            await ipLockController.Create(TestInit1.ProjectId, new IpLockCreateParams(sessionRequestEx.ClientIp!) { IsLocked = true });
            var sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
            Assert.AreEqual(Common.Messaging.SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);

            // check unlock
            await ipLockController.Update(TestInit1.ProjectId, sessionRequestEx.ClientIp!.ToString(), new IpLockUpdateParams { IsLocked = false });
            sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
            Assert.AreNotEqual(Common.Messaging.SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);
        }
    }
}
