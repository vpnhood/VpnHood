using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using CertificateCreateParams = VpnHood.AccessServer.DTOs.CertificateCreateParams;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointGroupControllerTest : ControllerTest
{
    [TestMethod]
    public async Task CRUD()
    {
        var accessPointGroupController = new AccessPointGroupController(TestInit1.Http);
        var accessPointController = new AccessPointController(TestInit1.Http);
        
        //-----------
        // check: create
        //-----------
        var createData = new AccessPointGroupCreateParams { AccessPointGroupName = $"group 1 {Guid.NewGuid()}" };
        var accessPointGroup1A = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, createData);
        var publicIp1 = await TestInit1.NewIpV4();
        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId, new AccessPointCreateParams{ServerId = TestInit1.ServerId1,IpAddress = publicIp1.ToString(), AccessPointGroupId = accessPointGroup1A.AccessPointGroupId}); 
            
        var publicIp2 = await TestInit1.NewIpV4();
        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId, new AccessPointCreateParams{ServerId = TestInit1.ServerId1,IpAddress = publicIp2.ToString(), AccessPointGroupId = accessPointGroup1A.AccessPointGroupId});


        var accessPointGroup1B = await accessPointGroupController.AccessPointGroupsGetAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
        Assert.AreEqual(createData.AccessPointGroupName, accessPointGroup1B.AccessPointGroupName);
        var accessPoints = await accessPointController.AccessPointsGetAsync(TestInit1.ProjectId, accessPointGroupId: accessPointGroup1B.AccessPointGroupId);
        Assert.IsTrue(accessPoints!.Any(x => x.IpAddress == publicIp1.ToString()));
        Assert.IsTrue(accessPoints!.Any(x => x.IpAddress == publicIp2.ToString()));

        //-----------
        // check: update 
        //-----------
        var certificateController = TestInit1.CreateCertificateController();
        var certificate2 = await certificateController.Create(TestInit1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
        var updateParam = new AccessPointGroupUpdateParams
        {
            CertificateId = new GuidPatch{Value = certificate2.CertificateId},
            AccessPointGroupName = new StringPatch{Value = $"groupName_{Guid.NewGuid()}"}
        };
        await accessPointGroupController.AccessPointGroupsPatchAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId, updateParam);
        accessPointGroup1A = await accessPointGroupController.AccessPointGroupsGetAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
        Assert.AreEqual(updateParam.AccessPointGroupName.Value, accessPointGroup1A.AccessPointGroupName);
        Assert.AreEqual(updateParam.CertificateId.Value, accessPointGroup1A.CertificateId);

        //-----------
        // check: AlreadyExists exception
        //-----------
        try
        {
            await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId,
                new AccessPointGroupCreateParams { AccessPointGroupName = updateParam.AccessPointGroupName.Value });
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }

        //-----------
        // check: delete
        //-----------
        var accessPointGroup2 = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        await accessPointGroupController.AccessPointGroupsDeleteAsync(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
        try
        {
            await accessPointGroupController.AccessPointGroupsGetAsync(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }
    }
}