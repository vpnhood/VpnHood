using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessPointGroupClientTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();   
        var name = Guid.NewGuid().ToString();
        var farm1 = await AccessPointGroupDom.Create(testInit, name: name);
        var serverDom = await ServerDom.Create(testInit, null);

        //-----------
        // check: create
        //-----------
        var publicIp1 = await testInit.NewIpV4();
        await serverDom.AddAccessPoint(farm1.AccessPointGroupId, publicIp1);
            
        var publicIp2 = await testInit.NewIpV4();
        await serverDom.AddAccessPoint(farm1.AccessPointGroupId, publicIp2);

        await farm1.Reload();
        Assert.AreEqual(name, farm1.AccessPointGroup.AccessPointGroupName);

        var accessPoints = await farm1.GetAccessPoints();
        Assert.IsTrue(accessPoints.Any(x => x.IpAddress == publicIp1.ToString()));
        Assert.IsTrue(accessPoints.Any(x => x.IpAddress == publicIp2.ToString()));

        //-----------
        // check: update 
        //-----------
        var certificateClient = testInit.CertificatesClient;
        var certificate2 = await certificateClient.CreateAsync(farm1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
        var updateParam = new AccessPointGroupUpdateParams
        {
            CertificateId = new PatchOfGuid {Value = certificate2.CertificateId},
            AccessPointGroupName = new PatchOfString {Value = $"groupName_{Guid.NewGuid()}"}
        };
        
        await testInit.AccessPointGroupsClient.UpdateAsync(farm1.ProjectId, farm1.AccessPointGroupId, updateParam);
        await farm1.Reload();
        Assert.AreEqual(updateParam.AccessPointGroupName.Value, farm1.AccessPointGroup.AccessPointGroupName);
        Assert.AreEqual(updateParam.CertificateId.Value, farm1.AccessPointGroup.CertificateId);

        //-----------
        // check: AlreadyExists exception
        //-----------
        try
        {
            await AccessPointGroupDom.Create(testInit, name: farm1.AccessPointGroup.AccessPointGroupName);
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }

        //-----------
        // check: delete
        //-----------
        var farm2 = await AccessPointGroupDom.Create(serverCount: 0);
        await farm2.TestInit.AccessPointGroupsClient.DeleteAsync(farm2.ProjectId, farm2.AccessPointGroupId);
        try
        {
            await farm2.Reload();
            Assert.Fail("Exception Expected!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
        }
    }
}