using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TokenService_Test
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
        }

        [TestMethod]
        public async Task CRUD_public()
        {
            var tokenService = await TokenService.CreatePublic(tokenName: "tokenName", dnsName: "foo.test.vpnhood.com", serverEndPoint: "1.2.3.4", maxTraffic: 10);
            var token = await tokenService.GetToken();
            Assert.AreNotEqual(0, token.supportId);
            Assert.AreEqual("tokenName", token.tokenName);
            Assert.AreEqual("foo.test.vpnhood.com", token.dnsName);
            Assert.AreEqual("1.2.3.4", token.serverEndPoint);
            Assert.IsNull(token.startTime);
            Assert.IsNull(token.endTime);
            Assert.AreEqual(0, token.lifetime);
            Assert.AreEqual(0, token.maxClient);
            Assert.AreEqual(10, token.maxTraffic);
            Assert.IsTrue(token.isPublic);
        }

        [TestMethod]
        public async Task CRUD_private()
        {
            var tokenService = await TokenService.CreatePrivate(tokenName: "tokenName", dnsName: "foo.test.vpnhood.com",
            serverEndPoint: "1.2.3.4", maxTraffic: 10, maxClient: 5, endTime: new DateTime(2000, 1, 2), lifetime: 25);
            var token = await tokenService.GetToken();
            Assert.AreNotEqual(0, token.supportId);
            Assert.AreEqual("tokenName", token.tokenName);
            Assert.AreEqual("foo.test.vpnhood.com", token.dnsName);
            Assert.AreEqual("1.2.3.4", token.serverEndPoint);
            Assert.IsNull(token.startTime);
            Assert.AreEqual(new DateTime(2000, 1, 2), token.endTime);
            Assert.AreEqual(25, token.lifetime);
            Assert.AreEqual(5, token.maxClient);
            Assert.AreEqual(10, token.maxTraffic);
            Assert.IsFalse(token.isPublic);

        }

        [TestMethod]
        public async Task GetAccessUsage()
        {
            var clientIp1 = "1.1.1.1";
            var tokenService = await TokenService.CreatePublic(tokenName: "tokenName", dnsName: "foo.test.vpnhood.com", serverEndPoint: "1.2.3.4", maxTraffic: 10);

            var accessUsage = await tokenService.GetAccessUsage(clientIp: clientIp1);
            Assert.AreEqual(0, accessUsage.receivedTraffic);
            Assert.AreEqual(0, accessUsage.sentTraffic);

            accessUsage = await tokenService.GetAccessUsage(clientIp: clientIp1);
            Assert.AreEqual(0, accessUsage.receivedTraffic);
            Assert.AreEqual(0, accessUsage.sentTraffic);

            // GetClientInfo should return same result
            var clientInfo2 = await tokenService.AddAccessUsage(clientIp1, 0, 0);
            Assert.AreEqual(JsonConvert.SerializeObject(accessUsage), JsonConvert.SerializeObject(clientInfo2));
        }

        [TestMethod]
        public async Task AddAccessUsage()
        {
            // create token
            var tokenService = await TokenService.CreatePublic(tokenName: "public", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100);

            var clientIp1 = "1.1.1.1";
            var clientIp2 = "1.1.1.2";

            //--------------
            // check: zero usage
            //--------------
            var accessUsage = await tokenService.AddAccessUsage(clientIp: clientIp1, sentTraffic: 0, receivedTraffic: 0);
            Assert.AreEqual(0, accessUsage.sentTraffic);
            Assert.AreEqual(0, accessUsage.receivedTraffic);
            Assert.AreEqual(0, accessUsage.totalSentTraffic);
            Assert.AreEqual(0, accessUsage.totalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            accessUsage = await tokenService.AddAccessUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);
            Assert.AreEqual(5, accessUsage.sentTraffic);
            Assert.AreEqual(10, accessUsage.receivedTraffic);
            Assert.AreEqual(5, accessUsage.totalSentTraffic);
            Assert.AreEqual(10, accessUsage.totalReceivedTraffic);

            // again
            accessUsage = await tokenService.AddAccessUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);

            Assert.AreEqual(10, accessUsage.sentTraffic);
            Assert.AreEqual(20, accessUsage.receivedTraffic);
            Assert.AreEqual(10, accessUsage.totalSentTraffic);
            Assert.AreEqual(20, accessUsage.totalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            accessUsage = await tokenService.AddAccessUsage(clientIp: clientIp2, sentTraffic: 5, receivedTraffic: 10);

            Assert.AreEqual(5, accessUsage.sentTraffic);
            Assert.AreEqual(10, accessUsage.receivedTraffic);
            Assert.AreEqual(5, accessUsage.totalSentTraffic);
            Assert.AreEqual(10, accessUsage.totalReceivedTraffic);

            //-------------
            // check : add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using var conn = App.OpenConnection();
            conn.Execute(@$"DELETE FROM {PublicCycle.PublicCycle_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new { curentCycleId });

            accessUsage = await tokenService.AddAccessUsage(clientIp: clientIp1, sentTraffic: 5, receivedTraffic: 10);
            Assert.AreEqual(5, accessUsage.sentTraffic);
            Assert.AreEqual(10, accessUsage.receivedTraffic);
            Assert.AreEqual(15, accessUsage.totalSentTraffic);
            Assert.AreEqual(30, accessUsage.totalReceivedTraffic);

            //-------------
            // check : GetClientInfo should return same result
            //-------------
            var clientInfo2 = await tokenService.GetAccessUsage(clientIp1);
            Assert.AreEqual(JsonConvert.SerializeObject(accessUsage), JsonConvert.SerializeObject(clientInfo2));
        }
    }
}
