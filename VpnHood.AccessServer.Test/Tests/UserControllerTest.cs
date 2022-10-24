using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class UserClientTest : ClientTest
{
    [TestMethod]
    public async Task RegisterCurrentUser()
    {
        var userEmail = $"{Guid.NewGuid()}@gmail.com";

        // ------------
        // Check: New user should not exist if not he hasn't registered yet
        // ------------
        await TestInit1.SetHttpUser(userEmail, new Claim[]{new ("test_usage", "test")});
        var userClient = new UserClient(TestInit1.Http);
        try
        {
            await userClient.GetCurrentUserAsync();
            Assert.Fail("User should not exist!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            // ignored
        }

        // ------------
        // Check: Register current user
        // ------------
        await userClient.RegisterCurrentUserAsync();
        var user = await userClient.GetCurrentUserAsync();
        Assert.AreEqual(userEmail, user.Email);

        // Get Project List
        await TestInit1.ProjectClient.ListAsync();
    }
}