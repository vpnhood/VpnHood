using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class UserControllerTest : ControllerTest
{
    [TestMethod]
    public async Task RegisterCurrentUser()
    {
        var userEmail = $"{Guid.NewGuid()}@gmail.com";

        // ------------
        // Check: New user should not exist if not he hasn't registered yet
        // ------------
        TestInit1.SetHttpUser(userEmail);
        var userController = new UserController(TestInit1.Http);
        try
        {
            await userController.GetCurrentUserAsync();
            Assert.Fail("User should not exist!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            // ignored
        }

        // ------------
        // Check: Register current user
        // ------------
        await userController.RegisterCurrentUserAsync();
        var user = await userController.GetCurrentUserAsync();
        Assert.AreEqual(userEmail, user.Email);
    }
}