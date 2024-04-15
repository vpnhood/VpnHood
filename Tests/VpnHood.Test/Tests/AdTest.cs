using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Collections;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class AdTest : TestBase
{
    private class TestAdService(AdAccessManager accessManager) : IAppAdService
    {
        public bool FailOnNextTry { get; set; }

        public Task<string> ShowAd(CancellationToken cancellationToken)
        {
            if (FailOnNextTry)
                throw new Exception("Ad failed");

            var ret = Guid.NewGuid().ToString();
            accessManager.AddAdData(ret);
            return Task.FromResult(ret);
        }

        public void Dispose()
        {
        }
    }

    private class AdAccessManager(string storagePath, FileAccessManagerOptions options)
        : FileAccessManager(storagePath, options)
    {

        private readonly TimeoutDictionary<string, TimeoutItem> _adsData = new(TimeSpan.FromMinutes(10));

        public void AddAdData(string adData)
        {
            _adsData.TryAdd(adData, new TimeoutItem());
        }

        protected override TimeSpan? GetAddSessionExpiration(string? adData)
        {
            if (string.IsNullOrEmpty(adData))
                return null;

            return _adsData.TryRemove(adData, out _) 
                ? TimeSpan.FromMinutes(100) 
                : base.GetAddSessionExpiration(adData);
        }
    }

    [TestMethod]
    public async Task Session_must_be_closed_after_few_minutes_if_no_ad_is_seen()
    {
        // create server
        using var fileAccessManager = new AdAccessManager(TestHelper.CreateAccessManagerWorkingDir(), TestHelper.CreateFileAccessManagerOptions());
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        await using var server = TestHelper.CreateServer(testAccessManager);

        var accessItem = fileAccessManager.AccessItem_Create(isAdRequired: true);
        accessItem.Token.ToAccessKey();

        await using var app = TestHelper.CreateClientApp();
        app.Services.AdService = new TestAdService(fileAccessManager);
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        Assert.IsNotNull(app.State.SessionStatus?.AccessUsage);
        Assert.IsTrue(app.State.SessionStatus.AccessUsage.ExpirationTime > DateTime.UtcNow.AddMinutes(50));
    }
}