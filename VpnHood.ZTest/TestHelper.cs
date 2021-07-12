using VpnHood.Server;
using VpnHood.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.IO;
using VpnHood.Test.Factory;
using VpnHood.Server.AccessServers;
using VpnHood.Logging;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VpnHood.Client.App;
using System.Net.Http;
using VpnHood.Common;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;

namespace VpnHood.Test
{
    static class TestHelper
    {
        public static readonly Uri TEST_HttpsUri1 = new("https://www.quad9.net/");
        public static readonly Uri TEST_HttpsUri2 = new("https://www.shell.com/");
        public static readonly IPEndPoint TEST_NsEndPoint1 = IPEndPoint.Parse("9.9.9.9:53");
        public static readonly IPEndPoint TEST_NsEndPoint2 = IPEndPoint.Parse("149.112.112.112:53");
        public static readonly IPAddress TEST_PingAddress1 = IPAddress.Parse("9.9.9.9");
        public static readonly IPAddress TEST_PingAddress2 = IPAddress.Parse("1.1.1.1");
        public static readonly Uri TEST_InvalidUri = new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
        public static readonly IPAddress TEST_InvalidIp = IPAddress.Parse("192.168.199.199");
        public static readonly IPEndPoint TEST_InvalidEp = Util.ParseIpEndPoint("192.168.199.199:9999");

        public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");

        public static string CreateNewFolder(string namePart)
        {
            string folder = Path.Combine(WorkingPath, $"{namePart}_{Guid.NewGuid()}");
            Directory.CreateDirectory(folder);
            return folder;
        }

        internal static void Cleanup()
        {
            try
            {
                if (Directory.Exists(WorkingPath))
                    Directory.Delete(WorkingPath, true);
            }
            catch { }
        }

        public static void WaitForClientState(VpnHoodApp app, AppConnectionState connectionSate, int timeout = 4000)
        {
            var waitTime = 200;
            for (var elapsed = 0; elapsed < timeout && app.State.ConnectionState != connectionSate; elapsed += waitTime)
                Thread.Sleep(waitTime);

            Assert.AreEqual(connectionSate, app.State.ConnectionState);
        }

        public static void WaitForClientState(VpnHoodClient client, ClientState clientState, int timeout = 6000)
        {
            var waitTime = 200;
            for (var elapsed = 0; elapsed < timeout && client.State != clientState; elapsed += waitTime)
                Thread.Sleep(waitTime);

            Assert.AreEqual(clientState, client.State);
        }

        private static PingReply SendPing(Ping ping = null, IPAddress ipAddress = null, int timeout = 3000)
        {
            using var pingT = new Ping();
            if (ping == null) ping = pingT;
            var pingOptions = new PingOptions()
            {
                Ttl = TestPacketCapture.ServerPingTtl // set ttl to control by test adapter
            };

            return ping.Send(ipAddress ?? TEST_PingAddress1, timeout, new byte[100], pingOptions);
        }

        private static IPHostEntry SendUdp(UdpClient udpClient = null, IPEndPoint nsEndPoint = null, int timeout = 10000)
        {
            return DiagnoseUtil.GetHostEntry("www.google.com", nsEndPoint ?? TEST_NsEndPoint1, udpClient, timeout).Result;
        }

        private static bool SendHttpGet(HttpClient httpClient = null, Uri uri = null, int timeout = 3000)
        {
            using var httpClientT = new HttpClient();
            if (httpClient == null) httpClient = httpClientT;
            var task = httpClient.GetStringAsync(uri ?? TEST_HttpsUri1);
            if (!task.Wait(timeout))
                throw new TimeoutException("GetStringAsync timeout!");
            var result = task.Result;
            return result.Length > 100;
        }

        public static void Test_Ping(Ping ping = null, IPAddress ipAddress = null, int timeout = 3000)
        {
            var pingReply = SendPing(ping, ipAddress, timeout);
            Assert.AreEqual(IPStatus.Success, pingReply.Status);
        }

        public static void Test_Udp(UdpClient udpClient = null, IPEndPoint nsEndPoint = null, int timeout = 3000)
        {
            var hostEntry = SendUdp(udpClient, nsEndPoint, timeout);
            Assert.IsNotNull(hostEntry);
            Assert.IsTrue(hostEntry.AddressList.Length > 0);
        }

        public static void Test_Https(HttpClient httpClient = null, Uri uri = null, int timeout = 3000)
        {
            if (!SendHttpGet(httpClient, uri, timeout))
                throw new Exception("Https get doesn't work!");
        }

        private static IPAddress[] GetTestIpAddresses()
        {
            var addresses = new List<IPAddress>();
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri2.Host));
            addresses.Add(TEST_NsEndPoint1.Address);
            addresses.Add(TEST_NsEndPoint2.Address);
            addresses.Add(TEST_PingAddress1);
            addresses.Add(TEST_PingAddress2);
            addresses.Add(new ClientOptions().TcpProxyLoopbackAddress);
            return addresses.ToArray();
        }

        private static int _accessItemIndex = 0;
        public static FileAccessServer.AccessItem CreateAccessItem(VpnHoodServer server,
            int maxClientCount = 1,
            int maxTrafficByteCount = 0,
            DateTime? expirationTime = null
            )
        {
            var accessServer = (FileAccessServer)server.AccessServer;
            return accessServer.CreateAccessItem(
                publicEndPoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), server.TcpHostEndPoint.Port),
                tokenName: $"Test Server {++_accessItemIndex}",
                maxClientCount: maxClientCount,
                maxTrafficByteCount: maxTrafficByteCount,
                expirationTime: expirationTime
                );
        }

        public static VpnHoodServer CreateServer(IAccessServer accessServer = null, IPEndPoint tcpHostEndPoint = null)
        {
            VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
            if (accessServer == null)
                accessServer = new FileAccessServer(Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}"));

            // Create server
            var server = new VpnHoodServer(accessServer, new ServerOptions()
            {
                TcpHostEndPoint = tcpHostEndPoint ?? Util.GetFreeEndPoint(IPAddress.Any),
                SocketFactory = new TestSocketFactory()
            });

            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);

            return server;
        }

        public static IDevice CreateDevice(TestDeviceOptions options = null)
        {
            if (options == null) options = new TestDeviceOptions();
            if (options.TestIpAddresses == null) options.TestIpAddresses = GetTestIpAddresses();
            return new TestDevice(options);
        }

        public static IPacketCapture CreatePacketCapture(TestDeviceOptions options = null) 
            => new TestDevice(options).CreatePacketCapture().Result;

        public static VpnHoodClient CreateClient(Token token,
            IPacketCapture packetCapture = null,
            TestDeviceOptions deviceOptions = null,
            Guid? clientId = null,
            bool autoConnect = true,
            ClientOptions options = null)
        {

            if (packetCapture == null) packetCapture = CreatePacketCapture(deviceOptions);
            if (clientId == null) clientId = Guid.NewGuid();
            if (options == null) options = new ClientOptions();
            if (options.Timeout == new ClientOptions().Timeout) options.Timeout = 2000; //overwrite default timeout
            options.SocketFactory = new TestSocketFactory();

            var client = new VpnHoodClient(
              packetCapture: packetCapture,
              clientId: clientId.Value,
              token: token,
              options);

            // test starting the client
            if (autoConnect)
                client.Connect().Wait();

            return client;
        }

        public static VpnHoodConnect CreateClientConnect(Token token,
            IPacketCapture packetCapture = null,
            TestDeviceOptions deviceOptions = null,
            Guid? clientId = null,
            bool autoConnect = true,
            ClientOptions clientOptions = null,
            ConnectOptions connectOptions = null)
        {
            if (clientOptions == null) clientOptions = new ClientOptions();
            if (packetCapture == null) packetCapture = CreatePacketCapture(deviceOptions);
            if (clientId == null) clientId = Guid.NewGuid();
            if (clientOptions.Timeout == new ClientOptions().Timeout) clientOptions.Timeout = 2000; //overwrite default timeout
            clientOptions.SocketFactory = new Tunneling.Factory.SocketFactory();

            var clientConnect = new VpnHoodConnect(
              packetCapture: packetCapture,
              clientId: clientId.Value,
              token: token,
              clientOptions: clientOptions,
              connectOptions: connectOptions);

            // test starting the client
            if (autoConnect)
                clientConnect.Connect().Wait();

            return clientConnect;
        }

        public static VpnHoodApp CreateClientApp(string appPath = null, TestDeviceOptions deviceOptions = null)
        {
            if (deviceOptions == null) deviceOptions = new();

            //create app
            var appOptions = new AppOptions()
            {
                AppDataPath = appPath ?? Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
                LogToConsole = true,
                Timeout = 2000,
                SocketFactory = new TestSocketFactory()
            };

            var clientApp = VpnHoodApp.Init(new TestAppProvider(deviceOptions), appOptions);
            clientApp.Diagnoser.PingTtl = TestPacketCapture.ServerPingTtl;
            clientApp.Diagnoser.HttpTimeout = 2000;
            clientApp.Diagnoser.NsTimeout = 2000;

            return clientApp;
        }

    }
}
