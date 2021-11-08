using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessPointGroupControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task CRUD()
        {
            var accessPointGroupController = TestInit1.CreateAccessPointGroupController();
            AccessPointController accessPointController = TestInit1.CreateAccessPointController();

            //-----------
            // check: create
            //-----------
            var createData = new AccessPointGroupCreateParams { AccessPointGroupName = $"group 1 {Guid.NewGuid()}" };
            var accessPointGroup1A = await accessPointGroupController.Create(TestInit1.ProjectId, createData);
            var publicIp1 = await TestInit.NewIpV4();
            await accessPointController.Create(TestInit1.ProjectId, TestInit1.ServerId1,
                new AccessPointCreateParams(publicIp1)
                {
                    AccessPointGroupId = accessPointGroup1A.AccessPointGroupId
                });
            var publicIp2 = await TestInit.NewIpV4();
            await accessPointController.Create(TestInit1.ProjectId, TestInit1.ServerId1,
                new AccessPointCreateParams(publicIp2)
                {
                    AccessPointGroupId = accessPointGroup1A.AccessPointGroupId
                });


            var accessPointGroup1B = await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
            Assert.AreEqual(createData.AccessPointGroupName, accessPointGroup1B.AccessPointGroupName);
            Assert.IsTrue(accessPointGroup1B.AccessPoints!.Any(x => x.IpAddress == publicIp1.ToString()));
            Assert.IsTrue(accessPointGroup1B.AccessPoints!.Any(x => x.IpAddress == publicIp2.ToString()));

            //-----------
            // check: update 
            //-----------
            var certificateController = TestInit1.CreateCertificateController();
            var certificate2 = await certificateController.Create(TestInit1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
            var updateParam = new AccessPointGroupUpdateParams
            {
                CertificateId = certificate2.CertificateId,
                AccessPointGroupName = $"groupName_{Guid.NewGuid()}"
            };
            await accessPointGroupController.Update(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId, updateParam);
            accessPointGroup1A = await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
            Assert.AreEqual(updateParam.AccessPointGroupName, accessPointGroup1A.AccessPointGroupName);
            Assert.AreEqual(updateParam.CertificateId, accessPointGroup1A.CertificateId);

            //-----------
            // check: AlreadyExists exception
            //-----------
            try
            {
                await accessPointGroupController.Create(TestInit1.ProjectId,
                    new AccessPointGroupCreateParams { AccessPointGroupName = updateParam.AccessPointGroupName });
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            {
            }

            //-----------
            // check: delete
            //-----------
            var accessPointGroup2 = await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams());
            await accessPointGroupController.Delete(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
            try
            {
                await accessPointGroupController.Get(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (ex is not AssertFailedException)
            {
            }
        }
    }
}