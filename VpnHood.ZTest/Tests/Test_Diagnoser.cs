using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using VpnHood.Client.Exceptions;

namespace VpnHood.Test
{

    [TestClass]
    public class Test_Diagnoser
    {

        [TestMethod]
        public void Test_NormalConnect_NoInternet()
        {
            // create server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server).Token;
            token.ServerEndPoint = TestHelper.TEST_InvalidEp.ToString();

            // create client
            using var clientApp = TestHelper.CreateClientApp();
            var clientProfile = clientApp.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            // ************
            // NoInternetException
            clientApp.Diagnoser.TestHttpUris = new Uri[] { TestHelper.TEST_InvalidUri };
            clientApp.Diagnoser.TestNsIpEndPoints = new IPEndPoint[] { TestHelper.TEST_InvalidEp };
            clientApp.Diagnoser.TestPingIpAddresses = new IPAddress[] { TestHelper.TEST_InvalidIp };

            try { clientApp.Connect(clientProfile.ClientProfileId).Wait(); }
            catch (AggregateException ex) when (ex.InnerException is NoInternetException)
            {
            }
        }
    }
}
