using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
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
    public async Task AccessCode_reject_keeps_the_code_and_marks_it_refused()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var token1 = TestHelper.CreateAccessToken(server);

        // create access code and add it to test manager
        var accessCode = TestAppHelper.BuildAccessCode();

        // create access code
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.Premium = new AppPremiumOptions {
            AllowImportAccessCode = true
        };

        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = accessCode
        });

        // connect
        var ex = await Assert.ThrowsExactlyAsync<SessionException>(() => app.Connect(clientProfile.ClientProfileId));
        Assert.AreEqual(SessionErrorCode.AccessCodeRejected, ex.SessionResponse.ErrorCode);

        // The code is KEPT — refusal never deletes a credential (its issuer may extend it) — but
        // marked refused, so the profile stops claiming premium instead of failing every connect.
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsNotNull(clientProfile.AccessCode, "A refused access code must be kept on the profile.");
        Assert.IsNotNull(clientProfile.AccessCodeRefusal, "The refusal must be recorded on the profile.");
        Assert.AreEqual(SessionErrorCode.AccessCodeRejected, clientProfile.AccessCodeRefusal.ErrorCode);

        // typing a different code is a new credential — the old refusal is not its story
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = TestAppHelper.BuildAccessCode()
        });
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsNull(clientProfile.AccessCodeRefusal, "A changed code must clear the refused mark.");

        // code should not exist any return objects
        var hasAccessCode = ex.Data.Contains("AccessCode");
        Assert.IsFalse(hasAccessCode);
        Assert.AreNotEqual(true, app.State.LastError?.Data.ContainsKey("AccessCode"));
    }

    [TestMethod]
    public async Task A_refused_code_is_kept_and_keeps_claiming_premium()
    {
        var randomId = Guid.NewGuid();
        var token = new Token {
            Name = "Refused Code Test",
            IssuedAt = DateTime.UtcNow,
            SupportId = "refused-code-test",
            TokenId = randomId.ToString(),
            Secret = randomId.ToByteArray(),
            IsPublic = true, // a CONNECT-style profile: premium comes solely from the code
            ServerToken = new ServerToken {
                HostEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
                CertificateHash = randomId.ToByteArray(),
                HostName = randomId.ToString(),
                HostPort = 443,
                Secret = randomId.ToByteArray(),
                CreatedTime = DateTime.UtcNow,
                IsValidHostName = false
            }
        };

        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId,
            new ClientProfileUpdateParams { AccessCode = TestAppHelper.BuildAccessCode() });
        Assert.IsTrue(app.ClientProfileService.Get(clientProfile.ClientProfileId).IsPremium);

        app.ClientProfileService.MarkAccessCodeRefused(clientProfile.ClientProfileId,
            SessionErrorCode.AccessExpired);
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsNotNull(clientProfile.AccessCode, "the code itself is kept — a refusal deletes nothing");
        Assert.IsNotNull(clientProfile.AccessCodeRefusal, "and the refusal is recorded beside it");
        Assert.IsTrue(clientProfile.IsPremium,
            "a refusal must NOT flip the local premium gates: doing so turns the build into its own " +
            "free edition — premium locations gone, promotion banner back — on nobody's decision " +
            "(keyring plan §8). The app announces the ending instead.");

        // revival proves itself: a successful premium session clears the mark
        app.ClientProfileService.ClearAccessCodeRefused(clientProfile.ClientProfileId);
        clientProfile = app.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsNull(clientProfile.AccessCodeRefusal);
        Assert.IsTrue(clientProfile.IsPremium);
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
        var clientProfileInfo = clientProfile.ToInfo(app.Features);
        Assert.IsTrue(clientProfileInfo.IsPremium);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.CanGoPremium);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByCode);
        Assert.IsFalse(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByPurchase);
        Assert.IsNull(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByRewardedAd);
        Assert.IsNull(clientProfileInfo.SelectedLocationInfo?.Options.PremiumByTrial);
    }
}