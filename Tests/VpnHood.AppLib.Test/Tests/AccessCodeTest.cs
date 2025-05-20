using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test.Packets;
using TcpPacket = PacketDotNet.TcpPacket;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class AccessCodeTest : TestAppBase
{
    [TestMethod]
    public async Task AaFoo()
    {
        var buffer2 = new byte[6000];
        var count = 0xFFFFF;
        Console.WriteLine( (count * 1500) / 1_000_000_000 );
        var packet = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.11.12.13:53"), IPEndPoint.Parse("10.11.12.13:52"),
            null, new byte[1400]);
        packet.ExtractTcp().Checksum = 0;

        Random.Shared.NextBytes(packet.ExtractTcp().Payload.Span);

        var buffer = packet.Buffer.ToArray();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) {
            var ipPacket = NetPacketBuilder.Parse(buffer);
            var tcpPacket = ipPacket.Extract<TcpPacket>();
            tcpPacket.SourcePort = tcpPacket.DestinationPort;
            tcpPacket.DestinationPort = tcpPacket.SourcePort;
            ipPacket.DestinationAddress = ipPacket.SourceAddress;
            ipPacket.SourceAddress = ipPacket.DestinationAddress;
            //ipPacket.DestinationAddress = ipPacket.SourceAddress;
            //ipPacket.SourceAddress = ipPacket.DestinationAddress;
            ipPacket.UpdateAllChecksums();
            //ipPacket.UpdateAllChecksums();
            //ipPacket.UpdateCalculatedValues();
            var z = ipPacket.Bytes;
        }
        Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms");


        sw.Restart();
        for (var i = 0; i < count; i++) {
            var ipPacket = new IpV4Packet(buffer);
            var tcpPacket = ipPacket.ExtractTcp();
            tcpPacket.SourcePort = tcpPacket.DestinationPort;
            tcpPacket.DestinationPort = tcpPacket.SourcePort;
            ipPacket.DestinationAddress = ipPacket.SourceAddress;
            ipPacket.SourceAddress = ipPacket.DestinationAddress;
   

            //ipPacket.DestinationAddress = ipPacket.SourceAddress;
            //ipPacket.SourceAddress = ipPacket.DestinationAddress;
            ipPacket.UpdateAllChecksums();
            //ipPacket.UpdateAllChecksums();
            var z = ipPacket.Buffer;
            ipPacket.GetUnderlyingBufferUnsafe(buffer2, out _, out _);
        }
        Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms");

        var ipPacketVh = (IpV4Packet)PacketBuilder.Parse(buffer);
        ipPacketVh.UpdateAllChecksums();
        var ipPacketNet = (IPv4Packet)NetPacketBuilder.Parse(buffer);
        ipPacketNet.UpdateAllChecksums();

        Console.WriteLine($"IpNet: {ipPacketNet.Checksum}, IpVh: {ipPacketVh.HeaderChecksum}");
        Console.WriteLine($"TcpNet: {ipPacketNet.ExtractTcp().Checksum}, IpVh: {ipPacketVh.ExtractTcp().Checksum}");

        await Task.CompletedTask;
    }

    void Foo(IpPacket ipPacket, int dateLen = 10)
    {
        Span<byte> pseudoHeader = stackalloc byte[12];
        ipPacket.SourceAddressSpan.CopyTo(pseudoHeader[..4]);
        ipPacket.DestinationAddressSpan.CopyTo(pseudoHeader[4..8]);
        pseudoHeader[8] = 0;
        pseudoHeader[9] = (byte)IpProtocol.Tcp;
        pseudoHeader[10] = (byte)(dateLen >> 8);
        pseudoHeader[11] = (byte)(dateLen);
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
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = accessCode
        });

        // connect
        var ex = await Assert.ThrowsExceptionAsync<SessionException>(() => app.Connect(clientProfile.ClientProfileId));
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
        Assert.ThrowsException<ArgumentException>(() => app.ClientProfileService.Update(
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
}