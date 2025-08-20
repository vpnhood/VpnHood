using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.SocksProxy.Socks5Proxy;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class AccessCodeTest : TestAppBase
{
    [TestMethod]
    public async Task AaFoo()
    {
        var tcpClient = new TcpClient();
        Console.WriteLine(tcpClient.ReceiveBufferSize);
        Console.WriteLine(tcpClient.SendBufferSize);
        await Task.CompletedTask;


        Socks5Client socks5Client = new Socks5Client(new Socks5Options {
            ProxyEndPoint = new IPEndPoint(IPAddress.Parse("95.164.5.204"), 1080),
            Username = "proxyuser",
            Password = "zina2000"
        });
        var tcpClient2 = new TcpClient();
        //await socks5Client.ConnectAsync(tcpClient2, IPEndPoint.Parse("104.21.8.124:443"), CancellationToken.None);
        var udpEndPoint = await socks5Client.CreateUdpAssociateAsync(tcpClient2, cancellationToken: CancellationToken.None);
        Console.WriteLine(udpEndPoint);
        var dns = DnsResolver.BuildDnsQuery(2, "www,vpnhood.com");
        var buffer = new byte[1024];
        var size = Socks5Client.WriteUdpRequest(buffer, IPEndPoint.Parse("8.8.8.8:53"), dns);
        var udpClient = new UdpClient();
        udpClient.Connect(udpEndPoint);
        var r = await udpClient.SendAsync(buffer[..size]);
        Console.WriteLine(r);
        await Task.Delay(3000);
    }

    async Task Read(UdpClient udpClient)
    {
        var response = await udpClient.ReceiveAsync();
        Console.WriteLine(response);
        Console.WriteLine("ssss");
    }


    [TestMethod]
    public async Task AccessCode_Accept()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var token1 = TestHelper.CreateAccessToken(server);
        var token2 = TestHelper.CreateAccessToken(server, maxClientCount: 6);

        // create access code and add it to test manager
        var accessCode = TestAppHelper.BuildAccessCode();
        accessManager.AccessCodes.Add(accessCode, token2.TokenId);

        // create access code
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = AccessCodeUtils.Format(accessCode) // make sure it accept format
        });

        // connect
        await app.Connect(clientProfile.ClientProfileId);
        Assert.AreEqual(6, app.State.SessionInfo?.AccessInfo?.MaxDeviceCount,
            "token2 must be used instead of token1 due the access code.");
    }

    [TestMethod]
    public async Task AccessCode_reject_and_remove_from_profile()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var token1 = TestHelper.CreateAccessToken(server);

        // create access code and add it to test manager
        var accessCode = TestAppHelper.BuildAccessCode();

        // create access code
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AutoRemoveExpiredPremium = true; // auto remove premium on access code reject

        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = accessCode
        });

        // connect
        var ex = await Assert.ThrowsExactlyAsync<SessionException>(() => app.Connect(clientProfile.ClientProfileId));
        Assert.AreEqual(SessionErrorCode.AccessCodeRejected, ex.SessionResponse.ErrorCode);

        // code must be removed
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);

        Assert.IsNull(clientProfile.AccessCode, "Access code must be removed from profile.");

        // code should not exist any return objects
        Assert.IsFalse(ex.Data.Contains("AccessCode"));
        Assert.IsFalse(app.State.LastError?.Data.ContainsKey("AccessCode") == true);
    }

    [TestMethod]
    public async Task AccessCode_FailedByChecksum()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var token = TestHelper.CreateAccessToken(server);
        var str = new StringBuilder(TestAppHelper.BuildAccessCode());
        str[1] = str[1] == '0' ? '1' : '0'; // destroy checksum
        var accessCode = str.ToString();

        // create access code
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ReSharper disable once AccessToDisposedClosure
        Assert.ThrowsExactly<ArgumentException>(() => app.ClientProfileService.Update(
            clientProfile.ClientProfileId, new ClientProfileUpdateParams { AccessCode = accessCode }));
    }

    [TestMethod]
    public async Task ClientProfile_with_access_code_must_be_premium()
    {
        await using var server = await TestHelper.CreateServer();

        // create token
        var defaultPolicy = new ClientPolicy {
            ClientCountries = ["*"],
            FreeLocations = ["US", "CA"],
            Normal = 10,
            PremiumByPurchase = true,
            PremiumByRewardedAd = 20,
            PremiumByTrial = 30
        };
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.ServerLocations = ["US/California"];
        token.ClientPolicies = [defaultPolicy];

        // create access code
        var accessCode = TestAppHelper.BuildAccessCode();
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = accessCode
        });

        // check account is 
        var clientProfileInfo = clientProfile.ToInfo();
        Assert.IsTrue(clientProfileInfo.IsPremiumAccount);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.CanGoPremium);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByCode);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByPurchase);
        Assert.IsNull(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByRewardedAd);
        Assert.IsNull(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByTrial);
    }

    [TestMethod]
    public async Task Socks5_Udp_DnsQuery_Works()
    {
        var options = new Socks5Options {
            ProxyEndPoint = new IPEndPoint(IPAddress.Parse("95.164.5.204"), 1080),
            Username = "proxyuser",
            Password = "zina2000"
        };

        // Control TCP
        using var controlTcp = new TcpClient();

        // Pre-create UDP socket & bind so we can announce correct port in UDP ASSOCIATE
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var localUdpEp = (IPEndPoint)udp.Client.LocalEndPoint!;

        var socks5 = new Socks5Client(options);

        // Perform UDP associate announcing our UDP port
        var relayEndPoint = await socks5.CreateUdpAssociateAsync(controlTcp, localUdpEp, CancellationToken.None);

        // Some proxies return 0.0.0.0; if so we must use the proxy's IP
        if (relayEndPoint.Address.Equals(IPAddress.Any))
            relayEndPoint = new IPEndPoint(options.ProxyEndPoint.Address, relayEndPoint.Port);

        // Build a DNS query (make sure hostname is valid; earlier test had a comma typo)
        var dnsQuery = DnsResolver.BuildDnsQuery(0x1234, "www.vpnhood.com");

        Span<byte> sendBuffer = stackalloc byte[512];
        var packetLen = Socks5Client.WriteUdpRequest(sendBuffer, IPEndPoint.Parse("8.8.8.8:53"), dnsQuery);

        // Send to proxy relay
        await udp.SendAsync(sendBuffer[..packetLen].ToArray(), relayEndPoint);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var receiveTask = udp.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, cts.Token));
        Assert.AreEqual(receiveTask, completed, "Timed out waiting for UDP response through SOCKS5.");

        var received = receiveTask.Result;
        var srcEp = Socks5Client.ParseUdpResponse(received.Buffer, out var payload);
        var res = DnsResolver.ParseDnsResponse(payload.ToArray(), 0x1234);
        Console.WriteLine(string.Join(",", res.AddressList.Select(x=>x.ToString())));


        // Basic DNS validation
        Assert.IsTrue(payload.Length >= 12, "DNS header too short.");
        var id = (ushort)((payload[0] << 8) | payload[1]);
        Assert.AreEqual(0x1234, id, "DNS transaction ID mismatch.");
    }
}