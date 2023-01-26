using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTokenTest : BaseTest
{
    [TestMethod]
    public async Task SupportCode_is_unique_per_project()
    {
        var testInit2 = await TestInit.Create();

        var accessTokenClient1 = TestInit1.AccessTokensClient;
        var accessToken11 = await accessTokenClient1.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        var accessTokenClient2 = testInit2.AccessTokensClient;
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
        // Check: Should not create a session with deleted access key
        //-----------
        var farm1 = await AccessPointGroupDom.Create();
        var testInit = farm1.TestInit;

        //-----------
        // check: create
        //-----------
        var expirationTime1 = DateTime.Today.AddDays(1);
        expirationTime1 = expirationTime1.AddMilliseconds(-expirationTime1.Millisecond);
        var accessTokenDom1 = await farm1.CreateAccessToken(new AccessTokenCreateParams
        {
            AccessPointGroupId = farm1.AccessPointGroupId,
            AccessTokenName = "tokenName1",
            Url = "https://foo.com/accessKey1",
            MaxTraffic = 11,
            MaxDevice = 12,
            Lifetime = 13,
            ExpirationTime = expirationTime1
        });
        Assert.AreNotEqual(0, accessTokenDom1.AccessToken.SupportCode);
        Assert.AreEqual("tokenName1", accessTokenDom1.AccessToken.AccessTokenName);
        Assert.AreEqual(farm1.AccessPointGroupId, accessTokenDom1.AccessToken.AccessPointGroupId);
        Assert.IsNull(accessTokenDom1.AccessToken.FirstUsedTime);
        Assert.AreEqual(expirationTime1, accessTokenDom1.AccessToken.ExpirationTime);
        Assert.AreEqual(11, accessTokenDom1.AccessToken.MaxTraffic);
        Assert.AreEqual(12, accessTokenDom1.AccessToken.MaxDevice);
        Assert.AreEqual(13, accessTokenDom1.AccessToken.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey1", accessTokenDom1.AccessToken.Url);

        var farm2 = await AccessPointGroupDom.Create(testInit);
        var expirationTime2 = DateTime.UtcNow.AddDays(2);
        var accessTokenDom2 = await farm2.CreateAccessToken(new AccessTokenCreateParams
        {
            AccessPointGroupId = farm2.AccessPointGroupId,
            AccessTokenName = "tokenName2",
            Url = "https://foo.com/accessKey2",
            MaxTraffic = 21,
            MaxDevice = 22,
            Lifetime = 23,
            ExpirationTime = expirationTime2,
            IsPublic = true
        });
        Assert.AreNotEqual(0, accessTokenDom2.AccessToken.SupportCode);
        Assert.AreEqual("tokenName2", accessTokenDom2.AccessToken.AccessTokenName);
        Assert.AreEqual(farm2.AccessPointGroupId, accessTokenDom2.AccessToken.AccessPointGroupId);
        Assert.IsNull(accessTokenDom2.AccessToken.FirstUsedTime);
        Assert.IsNull(accessTokenDom2.AccessToken.LastUsedTime);
        Assert.AreEqual(expirationTime2, accessTokenDom2.AccessToken.ExpirationTime);
        Assert.AreEqual(21, accessTokenDom2.AccessToken.MaxTraffic);
        Assert.AreEqual(22, accessTokenDom2.AccessToken.MaxDevice);
        Assert.AreEqual(23, accessTokenDom2.AccessToken.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey2", accessTokenDom2.AccessToken.Url);
        Assert.IsTrue(accessTokenDom2.AccessToken.IsEnabled);

        //-----------
        // check: get
        //-----------
        var accessToken2B = (await testInit.AccessTokensClient.GetAsync(testInit.ProjectId, accessTokenDom2.AccessTokenId))
            .AccessToken;
        Assert.IsTrue((accessToken2B.ExpirationTime!.Value - accessTokenDom2.AccessToken.ExpirationTime!.Value) < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessTokenDom2.AccessToken.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(accessTokenDom2.AccessToken.AccessPointGroupId, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(accessTokenDom2.AccessToken.AccessTokenName, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessTokenDom2.AccessToken.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessTokenDom2.AccessToken.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(accessTokenDom2.AccessToken.Lifetime, accessToken2B.Lifetime);
        Assert.AreEqual(accessTokenDom2.AccessToken.MaxDevice, accessToken2B.MaxDevice);
        Assert.AreEqual(accessTokenDom2.AccessToken.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(accessTokenDom2.AccessToken.Url, accessToken2B.Url);
        Assert.IsTrue(accessTokenDom2.AccessToken.IsEnabled);

        //-----------
        // check: update
        //-----------
        var updateParams = new AccessTokenUpdateParams
        {
            AccessTokenName = new PatchOfString { Value = $"new_name_{Guid.NewGuid()}" },
            AccessPointGroupId = new PatchOfGuid { Value = farm2.AccessPointGroupId },
            ExpirationTime = new PatchOfNullableDateTime { Value = DateTime.UtcNow.AddDays(4) },
            Lifetime = new PatchOfInteger { Value = 61 },
            MaxDevice = new PatchOfInteger { Value = 7 },
            MaxTraffic = new PatchOfLong { Value = 805004 },
            Url = new PatchOfString { Value = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com" },
            IsEnabled = new PatchOfBoolean { Value = false }
        };

        await testInit.AccessTokensClient.UpdateAsync(testInit.ProjectId, accessTokenDom2.AccessTokenId, updateParams);
        accessToken2B = (await testInit.AccessTokensClient.GetAsync(testInit.ProjectId, accessTokenDom2.AccessTokenId))
            .AccessToken;

        Assert.IsTrue(accessToken2B.ExpirationTime!.Value - updateParams.ExpirationTime.Value < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessTokenDom2.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(updateParams.AccessPointGroupId.Value, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(updateParams.AccessTokenName.Value, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessTokenDom2.AccessToken.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessTokenDom2.AccessToken.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(updateParams.Lifetime.Value, accessToken2B.Lifetime);
        Assert.AreEqual(updateParams.MaxDevice.Value, accessToken2B.MaxDevice);
        Assert.AreEqual(accessTokenDom2.AccessToken.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(updateParams.Url.Value, accessToken2B.Url);
        Assert.AreEqual(updateParams.IsEnabled.Value, accessToken2B.IsEnabled);

        //-----------
        // check: getAccessKey
        //-----------
        var secret2B = testInit.VhContext.AccessTokens
            .Single(x => x.AccessTokenId == accessToken2B.AccessTokenId)
            .Secret;

        var certificateData = await farm2.DefaultServer.AgentClient.GetSslCertificateData(farm2.DefaultServer.ServerConfig.TcpEndPoints.First());
        var x509Certificate2 = new X509Certificate2(certificateData);

        var accessKey = await farm2.TestInit.AccessTokensClient.GetAccessKeyAsync(testInit.ProjectId, accessToken2B.AccessTokenId);
        var token = Token.FromAccessKey(accessKey);
        Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.HostName);
        Assert.AreEqual(true, token.IsPublic);
        Assert.AreEqual(accessToken2B.AccessTokenName, token.Name);
        Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()),
            Convert.ToBase64String(token.CertificateHash));

        Assert.AreEqual(Convert.ToBase64String(secret2B), Convert.ToBase64String(token.Secret));
        Assert.IsFalse(token.HostEndPoints?.Any(x => x.Equals(farm1.DefaultServer.ServerConfig.TcpEndPoints.First())));
        Assert.IsTrue(token.HostEndPoints?.Any(x => x.Address.Equals(farm2.DefaultServer.ServerInfo.PublicIpAddresses.First())));
        Assert.AreEqual(accessToken2B.SupportCode, token.SupportId);

        //-----------
        // Check: Delete
        //-----------
        await testInit.AccessTokensClient.DeleteAsync(testInit.ProjectId, accessTokenDom2.AccessTokenId);
        try
        {
            await testInit.AccessTokensClient.GetAsync(testInit.ProjectId, accessTokenDom2.AccessTokenId);
            Assert.Fail("AccessToken should not exist!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        //-----------
        // Check: Should not be able to create a session by deleted token
        //-----------
        try
        {
            await accessTokenDom2.CreateSession();
            Assert.Fail("Not found expected.");
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
        var accessTokenClient = testInit2.AccessTokensClient;

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
        var accessTokenClient = TestInit1.AccessTokensClient;
        try
        {
            await accessTokenClient.CreateAsync(TestInit1.ProjectId,
                new AccessTokenCreateParams { AccessPointGroupId = testInit2.AccessPointGroupId1 });
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
        var accessTokenClient = TestInit1.AccessTokensClient;
        try
        {
            await accessTokenClient.UpdateAsync(TestInit1.ProjectId, TestInit1.AccessToken1.AccessTokenId,
                new AccessTokenUpdateParams
                { AccessPointGroupId = new PatchOfGuid { Value = testInit2.AccessPointGroupId1 } });
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
        var accessPointGroup =
            await TestInit1.AccessPointGroupsClient.CreateAsync(TestInit1.ProjectId,
                new AccessPointGroupCreateParams());
        var hostEndPoint = await TestInit1.NewEndPoint();

        await TestInit1.AccessPointsClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = hostEndPoint.Address.ToString(),
                TcpPort = hostEndPoint.Port,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = true
            });

        // Create new accessTokens
        var publicAccessToken = await TestInit1.AccessTokensClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true });
        var privateAccessToken = await TestInit1.AccessTokensClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false });

        // add usage
        var usageInfo = new UsageInfo { ReceivedTraffic = 10000000, SentTraffic = 10000000 };
        var agentClient = await TestInit1.CreateAgentClient(TestInit1.ServerId1, null);
        var publicSessionResponseEx =
            await agentClient.Session_Create(
                TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);

        // add usage by another session
        publicSessionResponseEx =
            await agentClient.Session_Create(
                TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(publicSessionResponseEx.SessionId, usageInfo);

        //private session
        var privateSessionResponseEx =
            await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(privateAccessToken,
                hostEndPoint: hostEndPoint));
        await agentClient.Session_AddUsage(privateSessionResponseEx.SessionId, usageInfo);
        await TestInit1.Sync();

        // list
        var accessTokenClient = TestInit1.AccessTokensClient;
        var accessTokens = await accessTokenClient.ListAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId,
            usageStartTime: TestInit1.CreatedTime.AddSeconds(-1));
        var publicItem = accessTokens.Single(x => x.AccessToken.AccessTokenId == publicAccessToken.AccessTokenId);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);

        // list by time
        accessTokens = await accessTokenClient.ListAsync(TestInit1.ProjectId,
            accessPointGroupId: accessPointGroup.AccessPointGroupId, usageStartTime: DateTime.UtcNow.AddDays(-2));
        publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
        Assert.AreEqual(usageInfo.SentTraffic * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(usageInfo.ReceivedTraffic * 3, publicItem.Usage?.ReceivedTraffic);
    }
}