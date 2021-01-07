using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace VpnHood.Test
{

    [TestClass]
    public class Test_Diagnoser
    {
        [TestMethod]
        public void Test_Network_Ping()
        {
            // create server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessItem(server).Token;

            // create client
            using var clientApp = TestHelper.CreateClientApp();
            var clientProfile = clientApp.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            // start diagnose
            clientApp.Connect(clientProfileId: clientProfile.ClientProfileId, diagnose: true).Wait();
        }

        //public void Test_Network_Udp()
        //{

        //}

        //public void Test_Network_Http()
        //{

        //}

        //public void Test_ServerPing()
        //{

        //}

        //public void Test_Vpn_Connection()
        //{

        //}

        //public void Test_Vpn_Network_Ping()
        //{

        //}

        //public void Test_Vpn_Network_Udp()
        //{

        //}

        //public void Test_Vpn_Network_Http()
        //{

        //}

    }
}
