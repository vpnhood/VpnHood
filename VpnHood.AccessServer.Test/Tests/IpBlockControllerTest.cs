using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class IpBlockControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Crud()
        {
            var ipBlockController =  TestInit2.CreateIpBlockController();

            //-----------
            // check: Create
            //-----------
            var createParams1 = new IpBlockCreateParams(await TestInit.NewIpV4())
            {
                IsBlocked = true,
                Description = "Zigma"
            };
            await ipBlockController.Create(TestInit2.ProjectId, createParams1);

            var createParams2 = new IpBlockCreateParams(await TestInit.NewIpV4())
            {
                IsBlocked = false,
                Description = "Zigma2"
            };
            await ipBlockController.Create(TestInit2.ProjectId, createParams2);

            //-----------
            // check: get
            //-----------
            var ipLock = await ipBlockController.Get(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            Assert.IsNotNull(ipLock.BlockedTime);
            Assert.AreEqual(createParams1.Description, ipLock.Description);

            ipLock = await ipBlockController.Get(TestInit2.ProjectId, createParams2.IpAddress.ToString());
            Assert.IsNull(ipLock.BlockedTime);
            Assert.AreEqual(createParams2.Description, ipLock.Description);

            //-----------
            // check: update
            //-----------
            var updateParams1 = new IpBlockUpdateParams { Description = Guid.NewGuid().ToString(), IsLocked = false };
            await ipBlockController.Update(TestInit2.ProjectId, createParams1.IpAddress.ToString(), updateParams1);
            ipLock = await ipBlockController.Get(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            Assert.IsNull(ipLock.BlockedTime);
            Assert.AreEqual(updateParams1.Description, ipLock.Description);

            var updateParams2 = new IpBlockUpdateParams { Description = Guid.NewGuid().ToString(), IsLocked = true };
            await ipBlockController.Update(TestInit2.ProjectId, createParams2.IpAddress.ToString(), updateParams2);
            ipLock = await ipBlockController.Get(TestInit2.ProjectId, createParams2.IpAddress.ToString());
            Assert.IsNotNull(ipLock.BlockedTime);
            Assert.AreEqual(updateParams2.Description, ipLock.Description);

            //-----------
            // check: list
            //-----------
            var ipBlocks = await ipBlockController.List(TestInit2.ProjectId);
            Assert.AreEqual(2, ipBlocks.Length);
            Assert.IsTrue(ipBlocks.Any(x=>x.Ip==createParams1.IpAddress.ToString()));
            Assert.IsTrue(ipBlocks.Any(x=>x.Ip== createParams2.IpAddress.ToString()));

            //-----------
            // check: delete
            //-----------
            await ipBlockController.Delete(TestInit2.ProjectId, createParams1.IpAddress.ToString());
            ipBlocks = await ipBlockController.List(TestInit2.ProjectId);
            Assert.AreEqual(1, ipBlocks.Length);
            Assert.IsFalse(ipBlocks.Any(x => x.Ip == createParams1.IpAddress.ToString()));
            Assert.IsTrue(ipBlocks.Any(x => x.Ip == createParams2.IpAddress.ToString()));
        }
    }
}
