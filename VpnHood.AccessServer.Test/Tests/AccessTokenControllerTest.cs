using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Apis;
using VpnHood.AccessServer.Exceptions;
using VpnHood.Common;
using AccessTokenCreateParams = VpnHood.AccessServer.DTOs.AccessTokenCreateParams;
using AccessTokenUpdateParams = VpnHood.AccessServer.DTOs.AccessTokenUpdateParams;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTokenControllerTest : ControllerTest
{
    [TestMethod]
    public async Task SupportCode_is_unique_per_project()
    {
        var testInit2 = await TestInit.Create();

        var accessTokenController1 = TestInit1.CreateAccessTokenController();
        var accessToken11 = await accessTokenController1.Create(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        var accessTokenController2 = testInit2.CreateAccessTokenController();
        var accessToken21 = await accessTokenController2.Create(testInit2.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });

        var accessToken12 = await accessTokenController1.Create(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });
        var accessToken22 = await accessTokenController2.Create(testInit2.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });

        Assert.AreEqual(accessToken11.SupportCode + 1, accessToken12.SupportCode);
        Assert.AreEqual(accessToken21.SupportCode + 1, accessToken22.SupportCode);
    }

    [TestMethod]
    public async Task CRUD()
    {
        //-----------
        // check: create
        //-----------
        var accessTokenController = TestInit1.CreateAccessTokenController();

        var endTime1 = DateTime.Today.AddDays(1);
        endTime1 = endTime1.AddMilliseconds(-endTime1.Millisecond);

        var accessToken1 = await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams
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
        var accessToken2A = await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams
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
        var accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId))
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
        AccessTokenUpdateParams updateParams = new()
        {
            AccessTokenName = $"new_name_{Guid.NewGuid()}",
            AccessPointGroupId = accessToken2A.AccessPointGroupId,
            EndTime = DateTime.UtcNow.AddDays(4),
            Lifetime = 61,
            MaxDevice = 7,
            MaxTraffic = 805004,
            Url = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com"
        };

        await accessTokenController.Update(TestInit1.ProjectId, accessToken2A.AccessTokenId, updateParams);
        accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId))
            .AccessToken;

        Assert.IsTrue(accessToken2B.EndTime!.Value - updateParams.EndTime.Value < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(updateParams.AccessPointGroupId, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(updateParams.AccessTokenName, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(updateParams.Lifetime, accessToken2B.Lifetime);
        Assert.AreEqual(updateParams.MaxDevice, accessToken2B.MaxDevice);
        Assert.AreEqual(accessToken2A.StartTime, accessToken2B.StartTime);
        Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(updateParams.Url, accessToken2B.Url);

        //-----------
        // check: getAccessKey
        //-----------
        var agentController = TestInit1.CreateAgentController();
        var certificateData = await agentController.GetSslCertificateData(TestInit1.HostEndPointG2S1.ToString());
        var x509Certificate2 = new X509Certificate2(certificateData);

        var accessKey = await accessTokenController.GetAccessKey(TestInit1.ProjectId, accessToken2B.AccessTokenId);
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
        await accessTokenController.Delete(accessToken2B.ProjectId, accessToken2B.AccessTokenId);
        try
        {
            await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId);
            Assert.Fail("AccessToken should not exist!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }

        {
        }
    }


    [TestMethod]
    public async Task Quota()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenController = testInit2.CreateAccessTokenController();

        //-----------
        // check: Create
        //-----------
        await accessTokenController.Create(testInit2.ProjectId,
            new AccessTokenCreateParams() { AccessPointGroupId = testInit2.AccessPointGroupId1 });
        var accessTokens = await accessTokenController.List(testInit2.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.AccessTokenCount = accessTokens.Length;
        try
        {
            await accessTokenController.Create(testInit2.ProjectId,
                new AccessTokenCreateParams() { AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail($"{nameof(QuotaException)} is expected");
        }
        catch (QuotaException)
        {
            // Ignore
        }
    }

    [TestMethod]
    public async Task Validate_create()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenController = TestInit1.CreateAccessTokenController();
        try
        {
            await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var testInit2 = await TestInit.Create();
        var accessTokenController = TestInit1.CreateAccessTokenController();
        try
        {
            await accessTokenController.Update(TestInit1.ProjectId, TestInit1.AccessToken1.AccessTokenId,
                new AccessTokenUpdateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
        }
    }

    [TestMethod]
    public async Task List()
    {
        // create a new group with new server endpoint
        var accessPointGroupController = new AccessPointGroupController(TestInit1.Http);
        var accessPointGroup = await accessPointGroupController.AccessPointGroupsPostAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        var hostEndPoint = await TestInit1.NewEndPoint();

        var accessPointController = new AccessPointController(TestInit1.Http);
        await accessPointController.AccessPointsPostAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1, IpAddress = hostEndPoint.Address.ToString(),
                TcpPort = hostEndPoint.Port,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                AccessPointMode = AccessPointMode.Public,
                IsListen = true
            });

        // Create new accessTokens
        var accessTokenControl = new AccessTokenController(TestInit1.Http);
        var publicAccessToken = await accessTokenControl.AccessTokensPostAsync(TestInit1.ProjectId, new Apis.AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true });
        var privateAccessToken = await accessTokenControl.AccessTokensPostAsync(TestInit1.ProjectId, new Apis.AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false });

        // add usage
        var usageInfo = new UsageInfo { ReceivedTraffic = 10000000, SentTraffic = 10000000 };
        var agentController = TestInit1.CreateAgentController2();
        var publicSessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx2(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentController.UsageAsync(publicSessionResponseEx.SessionId, false, usageInfo);
        await agentController.UsageAsync(publicSessionResponseEx.SessionId, false, usageInfo);

        // add usage by another session
        publicSessionResponseEx = await agentController.SessionsPostAsync( TestInit1.CreateSessionRequestEx2(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentController.UsageAsync(publicSessionResponseEx.SessionId, false, usageInfo);

        //private session
        var privateSessionResponseEx = await agentController.SessionsPostAsync(TestInit1.CreateSessionRequestEx2(privateAccessToken, hostEndPoint: hostEndPoint));
        await agentController.UsageAsync(privateSessionResponseEx.SessionId, false, usageInfo);
        await TestInit1.FlushCache();
        await TestInit1.SyncToReport();

        // list
        var accessTokenController = new  AccessTokenController(TestInit1.Http);
        var accessTokens = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId, usageStartTime: TestInit1.CreatedTime.AddSeconds(-1));
        var publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);

        // list by time
        accessTokens = await accessTokenController.AccessTokensGetAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId, usageStartTime: DateTime.UtcNow.AddDays(-2));
        publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);
    }

    [TestMethod]
    public async Task Devices()
    {
        var accessTokenController = TestInit1.CreateAccessTokenController();
        var data = await TestInit1.Fill();

        await TestInit1.SyncToReport();

        var deviceDatas = await accessTokenController.Devices(TestInit1.ProjectId, data.AccessTokens[0].AccessTokenId, TestInit1.CreatedTime);
        Assert.AreEqual(2, deviceDatas.Length);
    }
}