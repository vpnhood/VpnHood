﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.Exceptions;

namespace VpnHood.Test.Tests;

[TestClass]
public class DiagnoserTest : TestBase
{
    [TestMethod]
    public async Task NormalConnect_NoInternet()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [TestHelper.TEST_InvalidEp];

        // create client
        await using var clientApp = TestHelper.CreateClientApp();
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        clientApp.Diagnoser.TestHttpUris = [TestHelper.TEST_InvalidUri];
        clientApp.Diagnoser.TestNsIpEndPoints = [TestHelper.TEST_InvalidEp];
        clientApp.Diagnoser.TestPingIpAddresses = [TestHelper.TEST_InvalidIp];

        try
        {
            await clientApp.Connect(clientProfile.ClientProfileId);
        }
        catch (Exception ex) 
        {
            Assert.AreEqual(nameof(NoInternetException), ex.GetType().Name);
        }
    }
}