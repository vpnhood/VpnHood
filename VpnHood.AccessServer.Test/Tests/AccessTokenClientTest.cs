using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Common.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;
using VpnHood.Server;
using VpnHood.Common.Exceptions;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTokenClientTest : ClientTest
{
    [TestMethod]
    public async Task SupportCode_is_unique_per_project()
    {
        var testInit2 = await TestInit.Create();

        var accessTokenClient1 = new AccessTokenClient(TestInit1.Http);
        var accessToken11 = await accessTokenClient1.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        var accessTokenClient2 = new AccessTokenClient(testInit2.Http);
        var accessToken21 = await accessTokenClient2.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });

        var accessToken12 = await accessTokenClient1.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });
        var accessToken22 = await accessTokenClient2.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });

        Assert.AreEqual(accessToken11.SupportCode + 1, accessToken12.SupportCode);
        Assert.AreEqual(accessToken21.SupportCode + 1, accessToken22.SupportCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        //-----------
        // check: create
        //-----------
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);

        var endTime1 = DateTime.Today.AddDays(1);
        endTime1 = endTime1.AddMilliseconds(-endTime1.Millisecond);

        var accessToken1 = await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = TestInit1.AccessPointGroupId1,
            AccessTokenName = "tokenName1",
            Url = "https://foo.com/accessKey1",
            MaxTraffic = 11,
            MaxDevice = 12,
            Lifetime = 13,
            EndTime = endTime1
        });
        Assert.AreNotEqual(0, accessToken1.SupportCode);
        Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
        Assert.AreEqual(TestInit1.AccessPointGroupId1, accessToken1.AccessPointGroupId);
        Assert.IsNull(accessToken1.StartTime);
        Assert.AreEqual(endTime1, accessToken1.EndTime);
        Assert.AreEqual(11, accessToken1.MaxTraffic);
        Assert.AreEqual(12, accessToken1.MaxDevice);
        Assert.AreEqual(13, accessToken1.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

        var endTime2 = DateTime.UtcNow.AddDays(2);
        var accessToken2A = await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = TestInit1.AccessPointGroupId2,
            AccessTokenName = "tokenName2",
            Url = "https://foo.com/accessKey2",
            MaxTraffic = 21,
            MaxDevice = 22,
            Lifetime = 23,
            EndTime = endTime2,
            IsPublic = true
        });
        Assert.AreNotEqual(0, accessToken2A.SupportCode);
        Assert.AreEqual("tokenName2", accessToken2A.AccessTokenName);
        Assert.AreEqual(TestInit1.AccessPointGroupId2, accessToken2A.AccessPointGroupId);
        Assert.IsNull(accessToken2A.StartTime);
        Assert.AreEqual(endTime2, accessToken2A.EndTime);
        Assert.AreEqual(21, accessToken2A.MaxTraffic);
        Assert.AreEqual(22, accessToken2A.MaxDevice);
        Assert.AreEqual(23, accessToken2A.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);

        //-----------
        // check: get
        //-----------
        var accessToken2B = (await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId))
            .AccessToken;
        Assert.IsTrue((accessToken2B.EndTime!.Value - accessToken2A.EndTime!.Value) < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(accessToken2A.AccessPointGroupId, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(accessToken2A.AccessTokenName, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(accessToken2A.Lifetime, accessToken2B.Lifetime);
        Assert.AreEqual(accessToken2A.MaxDevice, accessToken2B.MaxDevice);
        Assert.AreEqual(accessToken2A.StartTime, accessToken2B.StartTime);
        Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(accessToken2A.Url, accessToken2B.Url);
        CollectionAssert.AreEqual(accessToken2A.Secret, accessToken2B.Secret);

        //-----------
        // check: update
        //-----------
        var updateParams = new AccessTokenUpdateParams
        {
            AccessTokenName = new PatchOfString {Value =  $"new_name_{Guid.NewGuid()}"},
            AccessPointGroupId = new PatchOfGuid {Value =  accessToken2A.AccessPointGroupId},
            EndTime = new PatchOfNullableDateTime {Value =  DateTime.UtcNow.AddDays(4)},
            Lifetime = new PatchOfInteger {Value =  61},
            MaxDevice = new PatchOfInteger {Value =  7},
            MaxTraffic = new PatchOfLong {Value =  805004},
            Url = new PatchOfString {Value = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com"}
        };

        await accessTokenClient.UpdateAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId, updateParams);
        accessToken2B = (await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId))
            .AccessToken;

        Assert.IsTrue(accessToken2B.EndTime!.Value - updateParams.EndTime.Value < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(updateParams.AccessPointGroupId.Value, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(updateParams.AccessTokenName.Value, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(updateParams.Lifetime.Value, accessToken2B.Lifetime);
        Assert.AreEqual(updateParams.MaxDevice.Value, accessToken2B.MaxDevice);
        Assert.AreEqual(accessToken2A.StartTime, accessToken2B.StartTime);
        Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(updateParams.Url.Value, accessToken2B.Url);

        //-----------
        // check: getAccessKey
        //-----------
        var agentClient = TestInit1.CreateAgentClient();
        var certificateData = await agentClient.GetSslCertificateData(TestInit1.HostEndPointG2S1);
        var x509Certificate2 = new X509Certificate2(certificateData);

        var accessKey = await accessTokenClient.GetAccessKeyAsync(TestInit1.ProjectId, accessToken2B.AccessTokenId);
        var token = Token.FromAccessKey(accessKey);
        Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.HostName);
        Assert.AreEqual(true, token.IsPublic);
        Assert.AreEqual(accessToken2B.AccessTokenName, token.Name);
        Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()),
            Convert.ToBase64String(token.CertificateHash));
        Assert.AreEqual(Convert.ToBase64String(accessToken2B.Secret), Convert.ToBase64String(token.Secret));
        Assert.IsFalse(token.HostEndPoints?.Any(x => x.Equals(TestInit1.HostEndPointG1S1)));
        Assert.IsTrue(token.HostEndPoints?.Any(x => x.Equals(TestInit1.HostEndPointG2S2)));
        Assert.AreEqual(accessToken2B.SupportCode, token.SupportId);

        //-----------
        // Check: getAccessKey
        //-----------
        await accessTokenClient.DeleteAsync(accessToken2B.ProjectId, accessToken2B.AccessTokenId);
        try
        {
            await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId);
            Assert.Fail("AccessToken should not exist!");
        }
        catch (ApiException ex) 
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }


    [TestMethod]
    public async Task Quota()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenClient = new AccessTokenClient(testInit2.Http);

        //-----------
        // check: Create
        //-----------
        await accessTokenClient.CreateAsync(testInit2.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
        var accessTokens = await accessTokenClient.ListAsync(testInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.AccessTokenCount = accessTokens.Count;
        try
        {
            await accessTokenClient.CreateAsync(testInit2.ProjectId,
                new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail($"{nameof(QuotaException)} was expected.");
        }
        catch (ApiException ex) 
        {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_create()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
        try
        {
            await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenClient = new AccessTokenClient(TestInit1.Http);
        try
        {
            await accessTokenClient.UpdateAsync(TestInit1.ProjectId, TestInit1.AccessToken1.AccessTokenId,
                new AccessTokenUpdateParams { AccessPointGroupId = new PatchOfGuid {Value =  testInit2.AccessPointGroupId1 }});
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) 
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task List()
    {
        // create a new group with new server endpoint
        var accessPointGroup = await TestInit1.AccessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        var hostEndPoint = await TestInit1.NewEndPoint();

        await TestInit1.AccessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1, IpAddress = hostEndPoint.Address.ToString(),
                TcpPort = hostEndPoint.Port,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                AccessPointMode = AccessPointMode.Public,
                IsListen = true
            });

        // Create new accessTokens
        var publicAccessToken = await TestInit1.AccessTokenClient.CreateAsync(TestInit1.ProjectId, 
            new AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true });
        var privateAccessToken = await TestInit1.AccessTokenClient.CreateAsync(TestInit1.ProjectId, 
            new AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false });

        // add usage
        var usageInfo = new UsageInfo { ReceivedTraffic = 10000000, SentTraffic = 10000000 };
        var agentClient = await TestInit1.CreateAgentClient(TestInit1.ServerId1, null);
        var publicSessionResponseEx = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);

        // add usage by another session
        publicSessionResponseEx = await agentClient.Session_Create( TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);

        //private session
        var privateSessionResponseEx = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(privateAccessToken, hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(privateSessionResponseEx.SessionId, usageInfo);
        await TestInit1.Sync();

        // list
        var accessTokenClient = new  AccessTokenClient(TestInit1.Http);
        var accessTokens = await accessTokenClient.ListAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId, usageStartTime: TestInit1.CreatedTime.AddSeconds(-1));
        var publicItem = accessTokens.Single(x => x.AccessToken.AccessTokenId==publicAccessToken.AccessTokenId);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);

        // list by time
        accessTokens = await accessTokenClient.ListAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId, usageStartTime: DateTime.UtcNow.AddDays(-2));
        publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);
    }

    [TestMethod]
    public async Task Devices()
    {
        var data = await TestInit1.Fill();

        await TestInit1.Sync();

        var deviceDatas = await TestInit1.DeviceClient.ListAsync(TestInit1.ProjectId, accessTokenId: data.AccessTokens[0].AccessTokenId, usageStartTime: TestInit1.CreatedTime);
        Assert.AreEqual(2, deviceDatas.Count);
    }
}