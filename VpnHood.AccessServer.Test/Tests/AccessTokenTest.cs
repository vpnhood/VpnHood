using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using Token = VpnHood.Common.Token;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AccessTokenTest
{
    [TestMethod]
    public async Task SupportCode_is_unique_per_project()
    {
        var farmP1 = await ServerFarmDom.Create();
        var farmP2 = await ServerFarmDom.Create();

        var accessTokenDom11 = await farmP1.CreateAccessToken();
        var accessTokenDom12 = await farmP1.CreateAccessToken();
        var accessTokenDom21 = await farmP2.CreateAccessToken();
        var accessTokenDom22 = await farmP2.CreateAccessToken();

        Assert.AreEqual(accessTokenDom11.AccessToken.SupportCode + 1, accessTokenDom12.AccessToken.SupportCode);
        Assert.AreEqual(accessTokenDom21.AccessToken.SupportCode + 1, accessTokenDom22.AccessToken.SupportCode);
    }

    [TestMethod]
    public async Task Crud()
    {
        var farm1 = await ServerFarmDom.Create();
        var testApp = farm1.TestApp;

        //-----------
        // check: create
        //-----------
        var expirationTime1 = DateTime.Today.AddDays(1);
        expirationTime1 = expirationTime1.AddMilliseconds(-expirationTime1.Millisecond);
        var createParam1 = new AccessTokenCreateParams {
            ServerFarmId = farm1.ServerFarmId,
            AccessTokenName = Guid.NewGuid().ToString(),
            Description = Guid.NewGuid().ToString(),
            MaxTraffic = 11,
            MaxDevice = 12,
            Lifetime = 13,
            ExpirationTime = expirationTime1,
            IsEnabled = true
        };
        var accessTokenDom1 = await farm1.CreateAccessToken(createParam1);
        Assert.AreNotEqual(0, accessTokenDom1.AccessToken.SupportCode);
        Assert.AreEqual(createParam1.AccessTokenName, accessTokenDom1.AccessToken.AccessTokenName);
        Assert.AreEqual(farm1.ServerFarmId, accessTokenDom1.AccessToken.ServerFarmId);
        Assert.IsNull(accessTokenDom1.AccessToken.FirstUsedTime);
        Assert.AreEqual(expirationTime1, accessTokenDom1.AccessToken.ExpirationTime);
        Assert.AreEqual(createParam1.MaxTraffic, accessTokenDom1.AccessToken.MaxTraffic);
        Assert.AreEqual(createParam1.MaxDevice, accessTokenDom1.AccessToken.MaxDevice);
        Assert.AreEqual(createParam1.Lifetime, accessTokenDom1.AccessToken.Lifetime);
        Assert.AreEqual(createParam1.Description, accessTokenDom1.AccessToken.Description);

        var farm2 = await ServerFarmDom.Create(testApp);
        var expirationTime2 = DateTime.UtcNow.AddDays(2);
        var createParam2 = new AccessTokenCreateParams {
            ServerFarmId = farm2.ServerFarmId,
            AccessTokenName = Guid.NewGuid().ToString(),
            Description = Guid.NewGuid().ToString(),
            MaxTraffic = 21,
            MaxDevice = 22,
            Lifetime = 23,
            ExpirationTime = expirationTime2,
            IsPublic = true,
            IsEnabled = true
        };
        var accessTokenDom2 = await farm2.CreateAccessToken(createParam2);
        Assert.AreNotEqual(0, accessTokenDom2.AccessToken.SupportCode);
        Assert.AreEqual(createParam2.AccessTokenName, accessTokenDom2.AccessToken.AccessTokenName);
        Assert.AreEqual(farm2.ServerFarmId, accessTokenDom2.AccessToken.ServerFarmId);
        Assert.IsNull(accessTokenDom2.AccessToken.FirstUsedTime);
        Assert.IsNull(accessTokenDom2.AccessToken.LastUsedTime);
        Assert.AreEqual(expirationTime2, accessTokenDom2.AccessToken.ExpirationTime);
        Assert.AreEqual(createParam2.MaxTraffic, accessTokenDom2.AccessToken.MaxTraffic);
        Assert.AreEqual(createParam2.MaxDevice, accessTokenDom2.AccessToken.MaxDevice);
        Assert.AreEqual(createParam2.Lifetime, accessTokenDom2.AccessToken.Lifetime);
        Assert.AreEqual(createParam2.Description, accessTokenDom2.AccessToken.Description);
        Assert.IsTrue(accessTokenDom2.AccessToken.IsEnabled);

        //-----------
        // check: get
        //-----------
        var accessToken2B =
            (await testApp.AccessTokensClient.GetAsync(testApp.ProjectId, accessTokenDom2.AccessTokenId))
            .AccessToken;
        Assert.IsTrue((accessToken2B.ExpirationTime!.Value - accessTokenDom2.AccessToken.ExpirationTime!.Value) <
                      TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessTokenDom2.AccessToken.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(accessTokenDom2.AccessToken.ServerFarmId, accessToken2B.ServerFarmId);
        Assert.AreEqual(accessTokenDom2.AccessToken.AccessTokenName, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessTokenDom2.AccessToken.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessTokenDom2.AccessToken.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(accessTokenDom2.AccessToken.Lifetime, accessToken2B.Lifetime);
        Assert.AreEqual(accessTokenDom2.AccessToken.MaxDevice, accessToken2B.MaxDevice);
        Assert.AreEqual(accessTokenDom2.AccessToken.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(accessTokenDom2.AccessToken.Description, accessToken2B.Description);
        Assert.IsTrue(accessTokenDom2.AccessToken.IsEnabled);

        //-----------
        // check: update
        //-----------
        var updateParams = new AccessTokenUpdateParams {
            AccessTokenName = new PatchOfString { Value = $"new_name_{Guid.NewGuid()}" },
            ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId },
            ExpirationTime = new PatchOfNullableDateTime { Value = DateTime.UtcNow.AddDays(4) },
            Lifetime = new PatchOfInteger { Value = 61 },
            MaxDevice = new PatchOfInteger { Value = 7 },
            MaxTraffic = new PatchOfLong { Value = 805004 },
            Description = new PatchOfString { Value = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com" },
            IsEnabled = new PatchOfBoolean { Value = false }
        };

        await testApp.AccessTokensClient.UpdateAsync(testApp.ProjectId, accessTokenDom2.AccessTokenId, updateParams);
        accessToken2B = (await testApp.AccessTokensClient.GetAsync(testApp.ProjectId, accessTokenDom2.AccessTokenId))
            .AccessToken;

        Assert.IsTrue(accessToken2B.ExpirationTime!.Value - updateParams.ExpirationTime.Value <
                      TimeSpan.FromSeconds(1));
        Assert.AreEqual(accessTokenDom2.AccessTokenId, accessToken2B.AccessTokenId);
        Assert.AreEqual(updateParams.ServerFarmId.Value, accessToken2B.ServerFarmId);
        Assert.AreEqual(updateParams.AccessTokenName.Value, accessToken2B.AccessTokenName);
        Assert.AreEqual(accessTokenDom2.AccessToken.ProjectId, accessToken2B.ProjectId);
        Assert.AreEqual(accessTokenDom2.AccessToken.IsPublic, accessToken2B.IsPublic);
        Assert.AreEqual(updateParams.Lifetime.Value, accessToken2B.Lifetime);
        Assert.AreEqual(updateParams.MaxDevice.Value, accessToken2B.MaxDevice);
        Assert.AreEqual(accessTokenDom2.AccessToken.SupportCode, accessToken2B.SupportCode);
        Assert.AreEqual(updateParams.Description.Value, accessToken2B.Description);
        Assert.AreEqual(updateParams.IsEnabled.Value, accessToken2B.IsEnabled);

        //-----------
        // check: getAccessKey
        //-----------
        var secret2B = testApp.VhContext.AccessTokens
            .Single(x => x.AccessTokenId == accessToken2B.AccessTokenId)
            .Secret;

        var certificateData = farm2.DefaultServer.ServerConfig.Certificates.First().RawData;
        var x509Certificate2 = new X509Certificate2(certificateData);

        var accessKey =
            await farm2.TestApp.AccessTokensClient.GetAccessKeyAsync(testApp.ProjectId, accessToken2B.AccessTokenId);
        var token = Token.FromAccessKey(accessKey);
        Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.ServerToken.HostName);
        Assert.AreEqual(accessToken2B.AccessTokenName, token.Name);
        Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()),
            Convert.ToBase64String(token.ServerToken.CertificateHash!));

        Assert.AreEqual(Convert.ToBase64String(secret2B), Convert.ToBase64String(token.Secret));
        Assert.IsFalse(token.ServerToken.HostEndPoints?.Any(x =>
            x.Equals(farm1.DefaultServer.ServerConfig.TcpEndPointsValue.First())));
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.Equals(farm2.DefaultServer.ServerInfo.PublicIpAddresses.First())));
        Assert.AreEqual(accessToken2B.SupportCode.ToString(), token.SupportId);

        //-----------
        // Check: Delete
        //-----------
        await testApp.AccessTokensClient.DeleteAsync(testApp.ProjectId, accessTokenDom2.AccessTokenId);
        try {
            await testApp.AccessTokensClient.GetAsync(testApp.ProjectId, accessTokenDom2.AccessTokenId);
            Assert.Fail("AccessToken should not exist!");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        //-----------
        // Check: Should not be able to create a session by deleted token
        //-----------
        try {
            await accessTokenDom2.CreateSession();
            Assert.Fail("Not found expected.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task GetAccessKey_fail_if_server_token_not_usable()
    {
        using var farm = await ServerFarmDom.Create(createParams: new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        }, serverCount: 0);

        var accessToken = await farm.CreateAccessToken();
        await VhTestUtil.AssertApiException(HttpStatusCode.BadRequest, accessToken.GetToken());
    }

    [TestMethod]
    public async Task GetAccessKey_fail_if_use_hostname_with_multiple_port()
    {
        // create farm with UseHostName
        using var farm = await ServerFarmDom.Create(createParams: new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        }, serverCount: 0);

        // add different port
        var serverDom = await farm.AddNewServer();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf {
                Value = [farm.TestApp.NewAccessPoint(farm.TestApp.NewEndPointIp6(4443))]
            }
        });

        serverDom = await farm.AddNewServer();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf {
                Value = [ farm.TestApp.NewAccessPoint(farm.TestApp.NewEndPoint())]
            }
        });

        var serverFarmData = await farm.Update(new ServerFarmUpdateParams {
            UseHostName = new PatchOfBoolean { Value = true }
        });

        Assert.IsNull(serverFarmData.ServerFarm.TokenUrl);
        Assert.IsNotNull(serverFarmData.ServerFarm.TokenError);
    }

    [TestMethod]
    public async Task GetAccessKey_ForIp()
    {
        using var farm = await ServerFarmDom.Create(createParams: new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        });

        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsFalse(token.ServerToken.IsValidHostName);
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any());


        await farm.Update(new ServerFarmUpdateParams { UseHostName = new PatchOfBoolean { Value = true } });
        token = await accessToken.GetToken();
        Assert.IsTrue(token.ServerToken.IsValidHostName);
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any());
    }

    [TestMethod]
    public async Task GetAccessKey_With_ServerLocations()
    {
        // create farm
        using var farm = await ServerFarmDom.Create(createParams: new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        }, serverCount: 0);

        // create servers
        await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.1"));
        await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.0.0.2"));
        await farm.AddNewServer(publicIpV4: IPAddress.Parse("10.1.0.3"));
        await farm.AddNewServer(publicIpV4: IPAddress.Parse("11.1.0.4"));
        await farm.AddNewServer(publicIpV4: IPAddress.Parse("11.2.0.5"));

        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsNotNull(token.ServerToken.ServerLocations);
        Assert.AreEqual(4, token.ServerToken.ServerLocations.Length);
        Assert.AreEqual("10/0", token.ServerToken.ServerLocations[0]);
        Assert.AreEqual("10/1", token.ServerToken.ServerLocations[1]);
        Assert.AreEqual("11/1", token.ServerToken.ServerLocations[2]);
        Assert.AreEqual("11/2", token.ServerToken.ServerLocations[3]);
    }

    [TestMethod]
    public async Task GetAccessKey_ForDomain()
    {
        var testApp = await TestApp.Create();
        using var farm = await ServerFarmDom.Create(testApp, createParams: new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        });

        await farm.Update(new ServerFarmUpdateParams {
            UseHostName = new PatchOfBoolean { Value = true }
        });

        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsTrue(token.ServerToken.IsValidHostName);
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any());


        await farm.Update(new ServerFarmUpdateParams { UseHostName = new PatchOfBoolean { Value = false } });
        token = await accessToken.GetToken();
        Assert.IsFalse(token.ServerToken.IsValidHostName);
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any());
    }


    [TestMethod]
    public async Task Quota()
    {
        using var farm = await ServerFarmDom.Create();
        await farm.CreateAccessToken();
        var accessTokens = await farm.TestApp.AccessTokensClient.ListAsync(farm.ProjectId);

        //-----------
        // check: Quota
        //-----------
        QuotaConstants.AccessTokenCount = accessTokens.Items.Count;
        try {
            await farm.CreateAccessToken();
            Assert.Fail($"{nameof(QuotaException)} was expected.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(QuotaException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_create()
    {
        var farm1 = await ServerFarmDom.Create();
        var farm2 = await ServerFarmDom.Create();
        try {
            await farm1.TestApp.AccessTokensClient.CreateAsync(farm1.ProjectId,
                new AccessTokenCreateParams { ServerFarmId = farm2.ServerFarmId });
            Assert.Fail("KeyNotFoundException is expected!");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Validate_update()
    {
        var farm1 = await ServerFarmDom.Create();
        var farm2 = await ServerFarmDom.Create();

        var accessTokenDom1 = await farm1.CreateAccessToken();
        await VhTestUtil.AssertApiException<NotExistsException>(accessTokenDom1.Update(new AccessTokenUpdateParams {
            ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId }
        }));
    }

    [TestMethod]
    public async Task List()
    {
        using var farm = await ServerFarmDom.Create();
        var accessTokenDom1 = await farm.CreateAccessToken(true);
        var accessTokenDom2 = await farm.CreateAccessToken();

        var traffic = new Traffic { Received = 10000000, Sent = 10000000 };

        // add usage by session1 of the public token
        var sessionDom1 = await accessTokenDom1.CreateSession();
        await sessionDom1.AddUsage(traffic);
        await sessionDom1.AddUsage(traffic);

        // add usage by session2 of the public token
        var sessionDom2 = await accessTokenDom1.CreateSession();
        await sessionDom2.AddUsage(traffic);

        // add usage by session of the private token
        var sessionDom3 = await accessTokenDom2.CreateSession();
        await sessionDom3.AddUsage(traffic);
        await farm.TestApp.Sync();

        // list
        var accessTokens = await farm.TestApp.AccessTokensClient.ListAsync(farm.TestApp.ProjectId,
            serverFarmId: farm.ServerFarmId,
            usageBeginTime: farm.TestApp.CreatedTime.AddSeconds(-1));

        var publicItem = accessTokens.Items.Single(x => x.AccessToken.AccessTokenId == accessTokenDom1.AccessTokenId);
        Assert.AreEqual(traffic.Sent * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(traffic.Received * 3, publicItem.Usage?.ReceivedTraffic);

        // list by time
        accessTokens = await farm.TestApp.AccessTokensClient.ListAsync(farm.TestApp.ProjectId,
            serverFarmId: farm.ServerFarmId, usageBeginTime: DateTime.UtcNow.AddDays(-2));
        publicItem = accessTokens.Items.First(x => x.AccessToken.IsPublic);
        Assert.AreEqual(traffic.Sent * 3, publicItem.Usage?.SentTraffic);
        Assert.AreEqual(traffic.Received * 3, publicItem.Usage?.ReceivedTraffic);
    }

    [TestMethod]
    public async Task Delete_Many()
    {
        using var farm = await ServerFarmDom.Create();
        var tokens1 = await farm.TestApp.AccessTokensClient.ListAsync(farm.ProjectId);

        var accessTokenDom1 = await farm.CreateAccessToken();
        var accessTokenDom2 = await farm.CreateAccessToken();
        var accessTokenDom3 = await farm.CreateAccessToken();

        await farm.TestApp.AccessTokensClient.DeleteManyAsync(farm.ProjectId,
            new[] { accessTokenDom1.AccessTokenId, accessTokenDom2.AccessTokenId });

        var tokens2 = await farm.TestApp.AccessTokensClient.ListAsync(farm.ProjectId);
        Assert.AreEqual(tokens1.Items.Count + 1, tokens2.Items.Count);
        Assert.IsFalse(tokens2.Items.Any(x => x.AccessToken.AccessTokenId == accessTokenDom1.AccessTokenId));
        Assert.IsFalse(tokens2.Items.Any(x => x.AccessToken.AccessTokenId == accessTokenDom2.AccessTokenId));
        Assert.IsTrue(tokens2.Items.Any(x => x.AccessToken.AccessTokenId == accessTokenDom3.AccessTokenId));
    }

    [TestMethod]
    public async Task Update_should_invalidate_its_cache()
    {
        var farm1 = await ServerFarmDom.Create();

        // create access token
        var accessTokenDom1 = await farm1.CreateAccessToken(new AccessTokenCreateParams {
            ServerFarmId = farm1.ServerFarmId,
            AccessTokenName = "tokenName1",
            MaxDevice = 12,
            Lifetime = 13,
            ExpirationTime = null,
            IsEnabled = true
        });

        // create session
        var sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage(); // make sure it comes into cache

        // update after creating session
        var expirationTime1 = DateTime.Today.AddDays(5);
        await accessTokenDom1.Update(
            new AccessTokenUpdateParams {
                ExpirationTime = new PatchOfNullableDateTime { Value = expirationTime1 }
            });

        // add usage to create the new expiration
        var sessionResponse = await sessionDom.AddUsage();
        Assert.AreEqual(expirationTime1, sessionResponse.AccessUsage?.ExpirationTime);
    }


    [TestMethod]
    public async Task Delete_should_invalidate_its_cache()
    {
        var farm1 = await ServerFarmDom.Create();

        // create access token
        var accessTokenDom1 = await farm1.CreateAccessToken(new AccessTokenCreateParams {
            ServerFarmId = farm1.ServerFarmId,
            AccessTokenName = "tokenName1",
            MaxDevice = 12,
            Lifetime = 13,
            ExpirationTime = null,
            IsEnabled = true
        });

        // create session
        var sessionDom = await accessTokenDom1.CreateSession();
        await sessionDom.AddUsage(); // make sure it comes into cache

        // delete access token
        await accessTokenDom1.TestApp.AccessTokensClient.DeleteAsync(farm1.ProjectId, accessTokenDom1.AccessTokenId);
        var sessionResponse = await sessionDom.AddUsage();
        Assert.AreEqual(SessionErrorCode.SessionClosed, sessionResponse.ErrorCode);
    }
}