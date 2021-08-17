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
using System.Linq;
using VpnHood.Common.Converters;

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
        public static readonly IPEndPoint TEST_NtpEndPoint1 = IPEndPoint.Parse("129.6.15.29:123");  // https://tf.nist.gov/tf-cgi/servers.cgi
        public static readonly IPEndPoint TEST_NtpEndPoint2 = IPEndPoint.Parse("129.6.15.30:123");  // https://tf.nist.gov/tf-cgi/servers.cgi
        public static readonly Uri TEST_InvalidUri = new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
        public static readonly IPAddress TEST_InvalidIp = IPAddress.Parse("192.168.199.199");
        public static readonly IPEndPoint TEST_InvalidEp = IPEndPointConverter.Parse("192.168.199.199:9999");

        public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");

        public static string CreateNewFolder(string namePart)
        {
            var folder = Path.Combine(WorkingPath, $"{namePart}_{Guid.NewGuid()}");
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

        public static void WaitForClientState(VpnHoodApp app, AppConnectionState connectionSate, int timeout = 5000)
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

        private static PingReply SendPing(Ping? ping = null, IPAddress? ipAddress = null, int timeout = 3000)
        {
            using var pingT = new Ping();
            if (ping == null) ping = pingT;
            var pingOptions = new PingOptions()
            {
                Ttl = TestNetProtector.ServerPingTtl // set ttl to control by test adapter
            };

            return ping.Send(ipAddress ?? TEST_PingAddress1, timeout, new byte[100], pingOptions);
        }

        private static bool SendHttpGet(HttpClient? httpClient = default, Uri? uri = default, int timeout = 3000)
        {
            using var httpClientT = new HttpClient();
            if (httpClient == null) httpClient = httpClientT;
            var task = httpClient.GetStringAsync(uri ?? TEST_HttpsUri1);
            if (!task.Wait(timeout))
                throw new TimeoutException("GetStringAsync timeout!");
            var result = task.Result;
            return result.Length > 100;
        }

        public static void Test_Ping(Ping? ping = default, IPAddress? ipAddress = default, int timeout = 3000)
        {
            var pingReply = SendPing(ping, ipAddress, timeout);
            //if (pingReply.Status == IPStatus.TimedOut)
              //  pingReply = SendPing(ping, ipAddress, timeout); //try again
            Assert.AreEqual(IPStatus.Success, pingReply.Status);
        }

        public static void Test_Dns(UdpClient? udpClient = null, IPEndPoint? nsEndPoint = default, int timeout = 3000)
        {
            var hostEntry = DiagnoseUtil.GetHostEntry("www.google.com", nsEndPoint ?? TEST_NsEndPoint1, udpClient, timeout).Result;
            Assert.IsNotNull(hostEntry);
            Assert.IsTrue(hostEntry.AddressList.Length > 0);
        }

        public static void Test_Udp(UdpClient? udpClient = null, IPEndPoint? ntpEndPoint = default, int timeout = 3000)
        {
            using var udpClientT = new UdpClient();
            if (udpClient == null) udpClient = udpClientT;
            var oldTimeOut = udpClient.Client.SendTimeout;
            udpClient.Client.ReceiveTimeout = timeout;

            var time = GetNetworkTimeByNTP(udpClient, ntpEndPoint ?? TEST_NtpEndPoint1);
            Assert.IsTrue(time > DateTime.Now.AddMinutes(-10));
        }

        public static void Test_Https(HttpClient? httpClient = default, Uri? uri = default, int timeout = 3000)
        {
            if (!SendHttpGet(httpClient, uri, timeout))
                throw new Exception("Https get doesn't work!");
        }

        public static IPAddress[] GetTestIpAddresses()
        {
            var addresses = new List<IPAddress>();
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri2.Host));
            addresses.Add(TEST_NsEndPoint1.Address);
            addresses.Add(TEST_NsEndPoint2.Address);
            addresses.Add(TEST_NtpEndPoint1.Address);
            addresses.Add(TEST_NtpEndPoint2.Address);
            addresses.Add(TEST_PingAddress1);
            addresses.Add(TEST_PingAddress2);
            addresses.Add(new ClientOptions().TcpProxyLoopbackAddress);
            return addresses.ToArray();
        }

        private static int _accessItemIndex = 0;
        public static Token CreateAccessToken(FileAccessServer fileAccessServer, IPEndPoint serverEndPoint,
            int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
        {
            return fileAccessServer.CreateAccessItem(
                publicEndPoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), serverEndPoint.Port),
                tokenName: $"Test Server {++_accessItemIndex}",
                maxClientCount: maxClientCount,
                maxTrafficByteCount: maxTrafficByteCount,
                expirationTime: expirationTime
                ).Token;
        }

        public static Token CreateAccessToken(VpnHoodServer server,
            int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
        {
            TestAccessServer testAccessServer = (TestAccessServer)server.AccessServer;
            return CreateAccessToken((FileAccessServer)testAccessServer.BaseAccessServer,
                server.TcpHostEndPoint, maxClientCount, maxTrafficByteCount, expirationTime);
        }

        public static FileAccessServer CreateFileAccessServer()
            => new(Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}"));

        public static VpnHoodServer CreateServer(IAccessServer? accessServer = null, IPEndPoint? tcpHostEndPoint = null,
            bool autoStart = true, long accessSyncCacheSize = 0)
        {
            VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
            bool autoDisposeAccessServer = false;
            if (accessServer == null)
            {
                accessServer = new TestAccessServer(CreateFileAccessServer());
                autoDisposeAccessServer = true;
            }

            // ser server options
            ServerOptions serverOptions = new()
            {
                TcpHostEndPoint = tcpHostEndPoint ?? Util.GetFreeEndPoint(IPAddress.Any),
                SocketFactory = new TestSocketFactory(true),
                SubscribeInterval = TimeSpan.FromMilliseconds(100),
                AutoDisposeAccessServer = autoDisposeAccessServer
            };
            if (accessSyncCacheSize != 0)
                serverOptions.AccessSyncCacheSize = accessSyncCacheSize;

            // Create server
            var server = new VpnHoodServer(accessServer, serverOptions);
            if (autoStart)
            {
                server.Start().Wait();
                Assert.AreEqual(ServerState.Started, server.State);
            }

            return server;
        }

        public static IDevice CreateDevice(TestDeviceOptions? options = default)
            => new TestDevice(options);

        public static IPacketCapture CreatePacketCapture(TestDeviceOptions? options = default)
            => CreateDevice(options).CreatePacketCapture().Result;

        public static VpnHoodClient CreateClient(Token token,
            IPacketCapture? packetCapture = default,
            TestDeviceOptions? deviceOptions = default,
            Guid? clientId = default,
            bool autoConnect = true,
            ClientOptions? options = default)
        {

            if (packetCapture == null) packetCapture = CreatePacketCapture(deviceOptions);
            if (clientId == null) clientId = Guid.NewGuid();
            if (options == null) options = new ClientOptions();
            if (options.Timeout == new ClientOptions().Timeout) options.Timeout = 3000; //overwrite default timeout
            options.SocketFactory = new TestSocketFactory(false);
            options.PacketCaptureIncludeIpRanges = GetTestIpAddresses().Select(x => new IpRange(x)).ToArray();

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
            IPacketCapture? packetCapture = default,
            TestDeviceOptions? deviceOptions = default,
            Guid? clientId = default,
            bool autoConnect = true,
            ClientOptions? clientOptions = default,
            ConnectOptions? connectOptions = default)
        {
            if (clientOptions == null) clientOptions = new ClientOptions();
            if (packetCapture == null) packetCapture = CreatePacketCapture(deviceOptions);
            if (clientId == null) clientId = Guid.NewGuid();
            if (clientOptions.Timeout == new ClientOptions().Timeout) clientOptions.Timeout = 2000; //overwrite default timeout
            clientOptions.SocketFactory = new Tunneling.Factory.SocketFactory();
            clientOptions.PacketCaptureIncludeIpRanges = GetTestIpAddresses().Select(x => new IpRange(x)).ToArray();

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

        public static VpnHoodApp CreateClientApp(string? appPath = default, TestDeviceOptions? deviceOptions = default)
        {
            //create app
            var appOptions = new AppOptions()
            {
                AppDataPath = appPath ?? Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
                LogToConsole = true,
                Timeout = 2000,
                SocketFactory = new TestSocketFactory(false),
            };

            var clientApp = VpnHoodApp.Init(new TestAppProvider(deviceOptions), appOptions);
            clientApp.Diagnoser.PingTtl = TestNetProtector.ServerPingTtl;
            clientApp.Diagnoser.HttpTimeout = 2000;
            clientApp.Diagnoser.NsTimeout = 2000;
            clientApp.UserSettings.PacketCaptureIpRangesFilterMode = FilterMode.Include;
            clientApp.UserSettings.PacketCaptureIpRanges = GetTestIpAddresses().Select(x => new IpRange(x)).ToArray();

            return clientApp;
        }

        private static DateTime GetNetworkTimeByNTP(UdpClient udpClient, IPEndPoint endPoint)
        {
            var ntpDataRequest = new byte[48];
            ntpDataRequest[0] = 0x1B; //LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only), Mode = 3 (Client Mode)

            udpClient.Send(ntpDataRequest, ntpDataRequest.Length, endPoint);
            var ntpData = udpClient.Receive(ref endPoint);

            ulong intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | (ulong)ntpData[43];
            ulong fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | (ulong)ntpData[47];

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
            var networkDateTime = (new DateTime(1900, 1, 1)).AddMilliseconds((long)milliseconds);

            return networkDateTime;
        }
    }
}
