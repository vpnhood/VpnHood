using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;
using VpnHood.Server;

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
        public async Task GetAccess_Status_Expired()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: new DateTime(1900, 1, 1), lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Expired, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_Status_TrafficOverflow()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 14, endTime: null, lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.TrafficOverflow, access.StatusCode);
        }

        private static AccessController CreateAccessController()
        {
            var accessController = new AccessController(TestUtil.CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = TestInit.CreateControllerContext()
            };
            return accessController;
        }

        [TestMethod]
        public async Task AddAccessUSage_set_expirationtime_first_use()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: null, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.IsTrue( (access.ExpirationTime.Value -DateTime.Now.AddDays(30)).TotalSeconds < 10  );
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_should_not_set_expiration_Time()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: null, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);
            Assert.IsNull(access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddUsage_should_not_retset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: expectedExpirationTime, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_Data()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);
            
            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual("foo.test.com", access.DnsName);
            Assert.AreEqual("1.2.3.4", access.ServerEndPoint);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);
        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var tokenService = await TokenService.CreatePrivate(tokenName: "private", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100, endTime: new DateTime(2040,1,1), lifetime: 0, maxClient: 2);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.2" };

            var accessController = CreateAccessController();

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            // again
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: add usage for client 2
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(15, access.SentTrafficByteCount);
            Assert.AreEqual(30, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);


            //-------------
            // check: GetAccess should return same result
            //-------------
            var access2 = await accessController.GetAccess(clientIdentity2);
            Assert.AreEqual(JsonConvert.SerializeObject(access), JsonConvert.SerializeObject(access2));
        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var tokenService = await TokenService.CreatePublic(tokenName: "public", dnsName: "foo.test.com",
                serverEndPoint: "1.2.3.4", maxTraffic: 100);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.2" };

            var accessController = CreateAccessController();

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            // again
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

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
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access2 = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(JsonConvert.SerializeObject(access), JsonConvert.SerializeObject(access2));

        }
    }
}
