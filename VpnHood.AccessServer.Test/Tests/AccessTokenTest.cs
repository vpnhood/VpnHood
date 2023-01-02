using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
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
        // check: create
        //-----------
        var accessTokenClient = TestInit1.AccessTokensClient;

        var expirationTime1 = DateTime.Today.AddDays(1);
        expirationTime1 = expirationTime1.AddMilliseconds(-expirationTime1.Millisecond);

        var accessToken1 = await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = TestInit1.AccessPointGroupId1,
            AccessTokenName = "tokenName1",
            Url = "https://foo.com/accessKey1",
            MaxTraffic = 11,
            MaxDevice = 12,
            Lifetime = 13,
            ExpirationTime = expirationTime1
        });
        Assert.AreNotEqual(0, accessToken1.SupportCode);
        Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
        Assert.AreEqual(TestInit1.AccessPointGroupId1, accessToken1.AccessPointGroupId);
        Assert.IsNull(accessToken1.FirstUsedTime);
        Assert.AreEqual(expirationTime1, accessToken1.ExpirationTime);
        Assert.AreEqual(11, accessToken1.MaxTraffic);
        Assert.AreEqual(12, accessToken1.MaxDevice);
        Assert.AreEqual(13, accessToken1.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

        var expirationTime2 = DateTime.UtcNow.AddDays(2);
        var accessToken2A = await accessTokenClient.CreateAsync(TestInit1.ProjectId, new AccessTokenCreateParams
        {
            AccessPointGroupId = TestInit1.AccessPointGroupId2,
            AccessTokenName = "tokenName2",
            Url = "https://foo.com/accessKey2",
            MaxTraffic = 21,
            MaxDevice = 22,
            Lifetime = 23,
            ExpirationTime = expirationTime2,
            IsPublic = true
        });
        Assert.AreNotEqual(0, accessToken2A.SupportCode);
        Assert.AreEqual("tokenName2", accessToken2A.AccessTokenName);
        Assert.AreEqual(TestInit1.AccessPointGroupId2, accessToken2A.AccessPointGroupId);
        Assert.IsNull(accessToken2A.FirstUsedTime);
        Assert.IsNull(accessToken2A.LastUsedTime);
        Assert.AreEqual(expirationTime2, accessToken2A.ExpirationTime);
        Assert.AreEqual(21, accessToken2A.MaxTraffic);
        Assert.AreEqual(22, accessToken2A.MaxDevice);
        Assert.AreEqual(23, accessToken2A.Lifetime);
        Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);
        Assert.IsTrue(accessToken2A.IsEnabled);

        //-----------
        // check: get
        //-----------
        var accessToken2B = (await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId))
            .AccessToken;
        Assert.IsTrue((accessToken2B.ExpirationTime!.Value - accessToken2A.ExpirationTime!.Value) < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(accessToken2A.AccessPointGroupId, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(accessToken2A.AccessTokenName, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(accessToken2A.Lifetime, accessToken2B.Lifetime);
        Assert.AreEqual(accessToken2A.MaxDevice, accessToken2B.MaxDevice);
        Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(accessToken2A.Url, accessToken2B.Url);
        Assert.IsTrue(accessToken2A.IsEnabled);

        //-----------
        // check: update
        //-----------
        var updateParams = new AccessTokenUpdateParams
        {
            AccessTokenName = new PatchOfString { Value = $"new_name_{Guid.NewGuid()}" },
            AccessPointGroupId = new PatchOfGuid { Value = accessToken2A.AccessPointGroupId },
            ExpirationTime = new PatchOfNullableDateTime { Value = DateTime.UtcNow.AddDays(4) },
            Lifetime = new PatchOfInteger { Value = 61 },
            MaxDevice = new PatchOfInteger { Value = 7 },
            MaxTraffic = new PatchOfLong { Value = 805004 },
            Url = new PatchOfString { Value = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com" },
            IsEnabled = new PatchOfBoolean { Value = false }
        };

        await accessTokenClient.UpdateAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId, updateParams);
        accessToken2B = (await accessTokenClient.GetAsync(TestInit1.ProjectId, accessToken2A.AccessTokenId))
            .AccessToken;

        Assert.IsTrue(accessToken2B.ExpirationTime!.Value - updateParams.ExpirationTime.Value < TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(updateParams.AccessPointGroupId.Value, accessToken2B.AccessPointGroupId);
        Assert.AreEqual(updateParams.AccessTokenName.Value, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(updateParams.Lifetime.Value, accessToken2B.Lifetime);
        Assert.AreEqual(updateParams.MaxDevice.Value, accessToken2B.MaxDevice);
        Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(updateParams.Url.Value, accessToken2B.Url);
        Assert.AreEqual(updateParams.IsEnabled.Value, accessToken2B.IsEnabled);

        //-----------
        // check: getAccessKey
        //-----------
        var secret2B = TestInit1.VhContext.AccessTokens
            .Single(x => x.AccessTokenId == accessToken2B.AccessTokenId)
            .Secret;

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

        Assert.AreEqual(Convert.ToBase64String(secret2B), Convert.ToBase64String(token.Secret));
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
                AccessPointMode = AccessPointMode.Public,
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