using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Test
{

    [TestClass]
    public class AccessController_Test
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
        public async Task GetAccess()
        {
            throw new NotImplementedException();
        }


        [TestMethod]
        public async Task AddUsage_Private()
        {
            throw new NotImplementedException();
        }


        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var tokenService = await TokenService.CreatePublic(tokenName: "public", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = IPAddress.Parse("1.1.1.1") };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = IPAddress.Parse("1.1.1.2") };

            var accessController = new AccessController((ILogger<AccessController>)TestUtil.CreateConsoleLogger(true));

            //--------------
            // check: zero usage
            //--------------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);

            //-----------
            // check: add usage
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);

            // again
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);

            //-----------
            // check: add usage for client 2
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);

            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using var conn = App.OpenConnection();
            await conn.ExecuteAsync(@$"DELETE FROM {PublicCycle.PublicCycle_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new
            {
                curentCycleId
            });

            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);

            //-------------
            // check: GetClientInfo should return same result
            //-------------
            var access2 = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(JsonConvert.SerializeObject(access), JsonConvert.SerializeObject(access2));

        }
    }
}
