using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        var userController = TestInit1.CreateUserController(userEmail);
        try
        {
            await userController.GetCurrentUser();
            Assert.Fail("User should not exist!");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            // ignored
        }

        // ------------
        // Check: Register current user
        // ------------
        await userController.RegisterCurrentUser();
        var user = await userController.GetCurrentUser();
        Assert.AreEqual(userEmail, user.Email);
    }
}