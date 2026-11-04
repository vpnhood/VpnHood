using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.App.Services.Proxies;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Generics;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// tvOS empties every folder an app may write when the device runs low on storage, and keeps only the app's
// defaults: what the app cannot rebuild lives there (AppOptionsContext.StorageProvider), the rest is made again.
[TestClass]
public class TvStorageTest : TestAppBase
{
    [TestMethod]
    public async Task A_tv_keeps_what_the_app_cannot_rebuild_when_its_folders_are_emptied()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = await TestHelper.CreateAccessToken(server);

        // the storage and the device-only folder under one root, as .NET lays them out on tvOS
        var root = Path.Combine(TestAppHelper.WorkingPath, "tv_" + Guid.CreateVersion7());
        var storagePath = Path.Combine(root, "app");
        var noBackupStoragePath = Path.Combine(root, "no-backup", "app");
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: storagePath,
            noBackupStoragePath: noBackupStoragePath, storageProvider: new TestKeyValueStorageProvider(root));

        // a first run: the terms accepted, a key added, a split list written
        string clientId;
        Guid vpnProfileId;
        await using (var app = TestAppHelper.CreateClientApp(appOptions)) {
            app.UserSettings.IsLicenseAccepted = true;
            app.Settings.Save();
            vpnProfileId = app.VpnProfileService.ImportAccessKey(token.ToAccessKey()).VpnProfileId;
            app.SettingsService.SplitDomainSettings.Excludes = "example.com";
            clientId = app.Descriptor.ClientId;
        }

        foreach (var path in new[] {
                     Path.Combine(storagePath, "settings.json"), Path.Combine(storagePath, "profiles"),
                     Path.Combine(storagePath, "splits"), Path.Combine(noBackupStoragePath, "install_id")
                 })
            Assert.IsFalse(Path.Exists(path), $"{Path.GetFileName(path)} is in the folders tvOS empties.");

        // tvOS empties the folders while the app is closed
        Directory.Delete(root, recursive: true);

        await using (var app = TestAppHelper.CreateClientApp(appOptions)) {
            Assert.IsTrue(app.UserSettings.IsLicenseAccepted);
            Assert.IsNotNull(app.VpnProfileService.FindById(vpnProfileId));
            Assert.AreEqual("example.com", app.SettingsService.SplitDomainSettings.Excludes);
            Assert.AreEqual(clientId, app.Descriptor.ClientId, "The server would count a new device.");
        }
    }

    [TestMethod]
    public void A_change_past_the_budget_fails_and_keeps_what_was_there()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "tv_" + Guid.CreateVersion7());
        var storageProvider = new TestKeyValueStorageProvider(root, budget: 1024);
        var filePath = Path.Combine(root, "app", "settings.json");
        storageProvider.Write(filePath, "small");

        Assert.ThrowsExactly<IOException>(() => storageProvider.Write(filePath, new string('x', 2048)));
        Assert.AreEqual("small", storageProvider.Read(filePath));

        // what a deletion frees is there to take again, and nothing outside the root is taken at all
        storageProvider.Delete(filePath);
        storageProvider.Write(Path.Combine(root, "app", "profiles", "vpn_profiles.json"), new string('x', 900));
        Assert.ThrowsExactly<ArgumentException>(() =>
            storageProvider.Write(Path.Combine(root, "..", "elsewhere.json"), "x"));
    }

    [TestMethod]
    public void A_deletion_that_fails_frees_nothing_of_the_budget()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "tv_" + Guid.CreateVersion7());
        var storageProvider = new RemovalFailingStorageProvider(root, budget: 1024);
        var filePath = Path.Combine(root, "app", "settings.json");
        storageProvider.Write(filePath, new string('x', 900));

        // the entry is still there, and still takes its part of the budget
        Assert.ThrowsExactly<InvalidOperationException>(() => storageProvider.Delete(filePath));
        Assert.ThrowsExactly<IOException>(() =>
            storageProvider.Write(Path.Combine(root, "app", "profiles", "vpn_profiles.json"), new string('x', 900)));
    }

    [TestMethod]
    public async Task The_proxies_the_person_added_come_back_when_tvOS_deletes_their_database()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "tv_" + Guid.CreateVersion7());
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: Path.Combine(root, "app"),
            storageProvider: new TestKeyValueStorageProvider(root));
        appOptions.IsVpnServiceDataPurgeable = true;

        // added by hand - one of them then turned off - and pasted as a list, one of which goes again
        await using (var app = TestAppHelper.CreateClientApp(appOptions)) {
            var proxies = app.Services.ProxyEndPointService;
            await proxies.Add(new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "192.0.2.1", Port = 1080 },
                TestCt);
            var turnedOff = await proxies.Add(
                new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "192.0.2.2", Port = 8080 }, TestCt);
            await proxies.Update(turnedOff.EndPoint.Id, new ProxyEndPoint {
                Protocol = ProxyProtocol.Http, Host = "192.0.2.2", Port = 8080, IsEnabled = false
            }, TestCt);
            await proxies.Import("socks5://192.0.2.3:1081\nsocks5://192.0.2.4:1082", TestCt);
            var removed = (await ListProxies(proxies)).Items.Single(x => x.EndPoint.Host == "192.0.2.4");
            await proxies.Delete(removed.EndPoint.Id, TestCt);
        }

        // tvOS deletes the database while the app is closed
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        Directory.Delete(
            Path.GetDirectoryName(VpnServiceFolders.GetProxyStoreFilePath(device.VpnServiceDataFolder)) ??
            throw new InvalidOperationException("The proxies' database has no folder."), recursive: true);

        await using (var app = TestAppHelper.CreateClientApp(appOptions, device)) {
            var endPoints = (await ListProxies(app.Services.ProxyEndPointService)).Items;
            CollectionAssert.AreEquivalent(new[] { "192.0.2.1", "192.0.2.2", "192.0.2.3" },
                endPoints.Select(x => x.EndPoint.Host).ToArray());
            Assert.IsFalse(endPoints.Single(x => x.EndPoint.Host == "192.0.2.2").EndPoint.IsEnabled,
                "A proxy the person turned off comes back on.");
        }
    }

    private Task<ListResult<AppProxyEndPointInfo>> ListProxies(AppProxyEndPointService proxyEndPointService)
    {
        return proxyEndPointService.ListProxies(search: null, includeSucceeded: true, includeFailed: true,
            includeUnknown: true, includeDisabled: true, recordIndex: null, recordCount: null, TestCt);
    }

    // a store whose every removal fails
    private sealed class RemovalFailingStorageProvider(string rootFolderPath, long budget)
        : TestKeyValueStorageProvider(rootFolderPath, budget)
    {
        protected override void RemoveValue(string key) =>
            throw new InvalidOperationException("The store failed the removal.");
    }
}
