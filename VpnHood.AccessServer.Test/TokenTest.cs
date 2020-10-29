using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Net;
using System.Transactions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public partial class TokenTest
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
        }

        [TestMethod]
        public void GetClientInfo()
        {
            var clientIp1 = "1.1.1.1";

            var tokenService = TokenService.CreatePublic(tokenName: "tokenName", dnsName: "foo.test.vpnhood.com", serverEndPoint: "1.2.3.4", maxTraffic: 10);

            var clientInfo = tokenService.GetClientInfo(clientIp: clientIp1);
            // Token
            Assert.AreEqual("tokenName", clientInfo.token.tokenName);
            Assert.AreEqual("foo.test.vpnhood.com", clientInfo.token.dnsName);
            Assert.AreEqual("1.2.3.4", clientInfo.token.serverEndPoint);
            Assert.IsNull(clientInfo.token.expirationTime);
            Assert.AreEqual(0, clientInfo.token.maxClientCount);
            Assert.AreEqual(10, clientInfo.token.maxTraffic);

            //ClientUsage
            Assert.AreEqual(0, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(0, clientInfo.clientUsage.sentTraffic);

            clientInfo = tokenService.GetClientInfo(clientIp: clientIp1);
            
            //Token
            Assert.IsNull(clientInfo.token.expirationTime);
            Assert.AreEqual(0, clientInfo.token.maxClientCount);
            Assert.AreEqual(10,clientInfo.token.maxTraffic);

            //ClientUsage
            Assert.AreEqual(0, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(0, clientInfo.clientUsage.sentTraffic);

            // GetClientInfo should return same result
            var clientInfo2 = tokenService.AddClientUsage(clientIp1, 0, 0);
            Assert.AreEqual(JsonConvert.SerializeObject(clientInfo), JsonConvert.SerializeObject(clientInfo2));
        }

        [TestMethod]
        public void AddClientUsage_public()
        {
            // create token
            var tokenService = TokenService.CreatePublic(tokenName: "public", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100);

            var clientIp1 = "1.1.1.1";
            var clientIp2 = "1.1.1.2";

            //--------------
            // check: zero usage
            //--------------
            var clientInfo = tokenService.AddClientUsage(clientIp: clientIp1, sentTraffic: 0, receivedTraffic: 0);
            Assert.AreEqual(0, clientInfo.clientUsage.sentTraffic);
            Assert.AreEqual(0, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(0, clientInfo.clientUsage.totalSentTraffic);
            Assert.AreEqual(0, clientInfo.clientUsage.totalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            clientInfo = tokenService.AddClientUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);
            Assert.AreEqual(5, clientInfo.clientUsage.sentTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(5, clientInfo.clientUsage.totalSentTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.totalReceivedTraffic);

            // again
            clientInfo = tokenService.AddClientUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);

            Assert.AreEqual(10, clientInfo.clientUsage.sentTraffic);
            Assert.AreEqual(20, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.totalSentTraffic);
            Assert.AreEqual(20, clientInfo.clientUsage.totalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            clientInfo = tokenService.AddClientUsage(clientIp: clientIp2, sentTraffic:  5, receivedTraffic: 10);
            
            Assert.AreEqual(5, clientInfo.clientUsage.sentTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(5, clientInfo.clientUsage.totalSentTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.totalReceivedTraffic);

            //-------------
            // check : add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using var conn = App.OpenConnection();
            conn.Execute(@$"DELETE FROM {PublicCycle.PublicCycle_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new { curentCycleId });

            clientInfo = tokenService.AddClientUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);
            Assert.AreEqual(5, clientInfo.clientUsage.sentTraffic);
            Assert.AreEqual(10, clientInfo.clientUsage.receivedTraffic);
            Assert.AreEqual(15, clientInfo.clientUsage.totalSentTraffic);
            Assert.AreEqual(30, clientInfo.clientUsage.totalReceivedTraffic);

            //-------------
            // check : GetClientInfo should return same result
            //-------------
            var clientInfo2 = tokenService.GetClientInfo(clientIp1);
            Assert.AreEqual(JsonConvert.SerializeObject(clientInfo), JsonConvert.SerializeObject(clientInfo2));

        }
    }
}
