using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointGroupClientTest : ClientTest
{
    [TestMethod]
    public async Task Crud()
    {
        var accessPointGroupClient = TestInit1.AccessPointGroupsClient;
        var accessPointClient = TestInit1.AccessPointsClient;
        
        //-----------
        // check: create
        //-----------
        var createData = new AccessPointGroupCreateParams { AccessPointGroupName = $"group 1 {Guid.NewGuid()}" };
        var accessPointGroup1A = await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, createData);
        var publicIp1 = await TestInit1.NewIpV4();
        await accessPointClient.CreateAsync(TestInit1.ProjectId, new AccessPointCreateParams{ServerId = TestInit1.ServerId1,IpAddress = publicIp1.ToString(), AccessPointGroupId = accessPointGroup1A.AccessPointGroupId}); 
            
        var publicIp2 = await TestInit1.NewIpV4();
        await accessPointClient.CreateAsync(TestInit1.ProjectId, new AccessPointCreateParams{ServerId = TestInit1.ServerId1,IpAddress = publicIp2.ToString(), AccessPointGroupId = accessPointGroup1A.AccessPointGroupId});


        var accessPointGroup1B = await accessPointGroupClient.GetAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
        Assert.AreEqual(createData.AccessPointGroupName, accessPointGroup1B.AccessPointGroupName);
        var accessPoints = await accessPointClient.ListAsync(TestInit1.ProjectId, accessPointGroupId: accessPointGroup1B.AccessPointGroupId);
        Assert.IsTrue(accessPoints.Any(x => x.IpAddress == publicIp1.ToString()));
        Assert.IsTrue(accessPoints.Any(x => x.IpAddress == publicIp2.ToString()));

        //-----------
        // check: update 
        //-----------
        var certificateClient = TestInit1.CertificatesClient;
        var certificate2 = await certificateClient.CreateAsync(TestInit1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
        var updateParam = new AccessPointGroupUpdateParams
        {
            CertificateId = new PatchOfGuid {Value = certificate2.CertificateId},
            AccessPointGroupName = new PatchOfString {Value = $"groupName_{Guid.NewGuid()}"}
        };
        await accessPointGroupClient.UpdateAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId, updateParam);
        accessPointGroup1A = await accessPointGroupClient.GetAsync(TestInit1.ProjectId, accessPointGroup1A.AccessPointGroupId);
        Assert.AreEqual(updateParam.AccessPointGroupName.Value, accessPointGroup1A.AccessPointGroupName);
        Assert.AreEqual(updateParam.CertificateId.Value, accessPointGroup1A.CertificateId);

        //-----------
        // check: AlreadyExists exception
        //-----------
        try
        {
            await accessPointGroupClient.CreateAsync(TestInit1.ProjectId,
                new AccessPointGroupCreateParams { AccessPointGroupName = updateParam.AccessPointGroupName.Value });
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }

        //-----------
        // check: delete
        //-----------
        var accessPointGroup2 = await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        await accessPointGroupClient.DeleteAsync(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
        try
        {
            await accessPointGroupClient.GetAsync(TestInit1.ProjectId, accessPointGroup2.AccessPointGroupId);
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }
    }
}