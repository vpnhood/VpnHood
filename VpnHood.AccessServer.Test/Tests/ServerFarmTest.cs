﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using GrayMint.Common.Exceptions;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerFarmTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var farm1 = await AccessPointGroupDom.Create(testInit,  serverCount: 0);
        var serverDom = await farm1.AddNewServer();
        await farm1.CreateAccessToken(true);
        await farm1.CreateAccessToken(true);

        //-----------
        // check: create
        //-----------
        var publicIp1 = await testInit.NewIpV4();
        var publicIp2 = await testInit.NewIpV4();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf
            {
                Value = new[]
                {
                    new AccessPoint
                    {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp1.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 0,
                    },
                    new AccessPoint
                    {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp2.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 0,
                    }
                }
            }
        });

        var accessFarmData = await farm1.Reload();
        Assert.AreEqual(farm1.AccessPointGroup.AccessPointGroupName, farm1.AccessPointGroup.AccessPointGroupName);
        Assert.AreEqual(1, accessFarmData.Summary!.ServerCount);
        Assert.AreEqual(2, accessFarmData.Summary!.TotalTokenCount);
        Assert.AreEqual(2, accessFarmData.Summary!.UnusedTokenCount);
        Assert.AreEqual(0, accessFarmData.Summary!.InactiveTokenCount);
        Assert.AreEqual(1, accessFarmData.Summary!.ServerCount);

        var accessTokenDom = await farm1.CreateAccessToken(true);
        var accessKey = await accessTokenDom.GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.HostEndPoints!.Any(x => x.Address.Equals(publicIp1)));
        Assert.IsTrue(token.HostEndPoints!.Any(x => x.Address.Equals(publicIp2)));

        //-----------
        // check: update 
        //-----------
        var certificateClient = testInit.CertificatesClient;
        var certificate2 = await certificateClient.CreateAsync(farm1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
        var updateParam = new AccessPointGroupUpdateParams
        {
            CertificateId = new PatchOfGuid { Value = certificate2.CertificateId },
            AccessPointGroupName = new PatchOfString { Value = $"groupName_{Guid.NewGuid()}" }
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
            await AccessPointGroupDom.Create(testInit,
                new AccessPointGroupCreateParams
                {
                    AccessPointGroupName = farm1.AccessPointGroup.AccessPointGroupName
                });
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(AlreadyExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Delete_farm_and_its_dependents()
    {
        var farm2 = await AccessPointGroupDom.Create(serverCount: 0);
        var accessTokenDom = await farm2.CreateAccessToken(true);
        await farm2.TestInit.AccessPointGroupsClient.DeleteAsync(farm2.ProjectId, farm2.AccessPointGroupId);
        try
        {
            await farm2.Reload();
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        try
        {
            await accessTokenDom.Reload();
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Fail_delete_a_farm_with_server()
    {
        var farm1 = await AccessPointGroupDom.Create(serverCount: 0);

        //-----------
        // check: can not delete a farm with server
        //-----------
        var farm2 = await AccessPointGroupDom.Create(farm1.TestInit, serverCount: 0);
        var serverDom = await farm2.AddNewServer();
        try
        {
            await farm2.TestInit.AccessPointGroupsClient.DeleteAsync(farm2.ProjectId, farm2.AccessPointGroupId);
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }

        // move server to farm1
        await farm2.TestInit.ServersClient.UpdateAsync(farm2.ProjectId, serverDom.ServerId, new ServerUpdateParams
        {
            AccessPointGroupId = new PatchOfNullableGuid { Value = farm1.AccessPointGroupId }
        });
        await farm2.TestInit.AccessPointGroupsClient.DeleteAsync(farm2.ProjectId, farm2.AccessPointGroupId);
        try
        {
            await farm2.Reload();
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }
}