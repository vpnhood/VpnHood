using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentAccessTest : BaseTest
{
    [TestMethod]
    public async Task Access_token_is_not_enabled()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(false);
        await accessTokenDom.TestInit.AccessTokensClient.UpdateAsync(accessPointGroupDom.TestInit.ProjectId, accessTokenDom.AccessTokenId,
            new AccessTokenUpdateParams
            {
                IsEnabled = new PatchOfBoolean { Value = false }
            });


        var sessionDom = await accessTokenDom.CreateSession(assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessLocked, sessionDom.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Access_token_expired_by_expiration_date()
    {
        var accessTokenClient = TestInit1.AccessTokensClient;

        // create accessTokenModel
        var accessToken = await accessTokenClient.CreateAsync(TestInit1.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                ExpirationTime = new DateTime(1900, 1, 1)
            });
        var agentClient = TestInit1.CreateAgentClient();

        var sessionResponseEx = await agentClient.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
        Assert.AreEqual(SessionErrorCode.AccessExpired, sessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Access_token_expired_by_lifetime()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(new AccessTokenCreateParams
        {
            Lifetime = 1
        });

        await accessTokenDom.CreateSession();

        // Shift FirstUseTime to one day before
        var accessTokenModel = await accessTokenDom.TestInit.VhContext.AccessTokens.SingleAsync(x =>
            x.AccessTokenId == accessTokenDom.AccessTokenId);
        accessTokenModel.FirstUsedTime = accessPointGroupDom.CreatedTime.AddHours(-25);
        await accessTokenDom.TestInit.VhContext.SaveChangesAsync();

        // Create new Session
        var session = await accessTokenDom.CreateSession(assertError: false);
        Assert.AreEqual(SessionErrorCode.AccessExpired, session.SessionResponseEx.ErrorCode);
    }

    [TestMethod]
    public async Task Access_token_firstUsedTime_and_lastUsedTime()
    {
        var accessPointGroupDom = await AccessPointGroupDom.Create();
        var accessTokenDom = await accessPointGroupDom.CreateAccessToken(false);
        Assert.IsNull(accessTokenDom.AccessToken.LastUsedTime);
        Assert.IsNull(accessTokenDom.AccessToken.FirstUsedTime);

        //-----------
        // Check: first time use
        //-----------
        var dateTime = DateTime.UtcNow;
        await Task.Delay(1100);
        await accessTokenDom.CreateSession();
        await accessTokenDom.Reload();
        Assert.IsTrue(accessTokenDom.AccessToken.LastUsedTime > dateTime);
        Assert.IsTrue(accessTokenDom.AccessToken.FirstUsedTime > dateTime);

        //-----------
        // Check: AddUsage should not update Access token LastUsedTime and FirstUsedTime due performance consideration
        //-----------
        var session = await accessTokenDom.CreateSession();
        await Task.Delay(1000);
        dateTime = DateTime.UtcNow; // for precious
        await session.AddUsage();
        await accessTokenDom.Reload();
        Assert.IsTrue(accessTokenDom.AccessToken.FirstUsedTime < dateTime, $"FirstUsedTime: {accessTokenDom.AccessToken.FirstUsedTime}, dateTime: {dateTime}");
        Assert.IsTrue(accessTokenDom.AccessToken.LastUsedTime < dateTime, $"FirstUsedTime: {accessTokenDom.AccessToken.FirstUsedTime}, dateTime: {dateTime}");

        //-----------
        // Check: Second usage should update Access LastUsedTime but not FirstUsedTime
        //-----------
        dateTime = DateTime.UtcNow;
        var oldAccessToken = accessTokenDom.AccessToken;
        await Task.Delay(1000);
        var sessionDom = await accessTokenDom.CreateSession();
        await accessTokenDom.Reload();
        Assert.AreEqual(accessTokenDom.AccessToken.FirstUsedTime, oldAccessToken.FirstUsedTime);
        Assert.IsTrue(accessTokenDom.AccessToken.LastUsedTime > dateTime);

        //-----------
        // Check: AddUsage should not update Access token UsedTime due performance consideration
        //-----------
        oldAccessToken = accessTokenDom.AccessToken;
        await Task.Delay(1000);
        await sessionDom.AddUsage(10);
        await accessTokenDom.Reload();
        Assert.AreEqual(accessTokenDom.AccessToken.LastUsedTime, oldAccessToken.LastUsedTime);
        Assert.AreEqual(accessTokenDom.AccessToken.LastUsedTime, oldAccessToken.LastUsedTime);
    }
}
