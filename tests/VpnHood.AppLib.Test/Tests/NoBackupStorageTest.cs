using System.Text.Json.Nodes;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Utils;

namespace VpnHood.AppLib.Test.Tests;

// What must not travel with a backup lives in the device-only storage a phone has
// (AppOptionsContext.NoBackupStoragePath); what a person wants back stays in the storage.
[TestClass]
public class NoBackupStorageTest : TestAppBase
{
    [TestMethod]
    [Obsolete("Migration (2026-10): remove with AppStorageMigration.")]
    public void An_update_moves_what_must_not_travel_and_leaves_the_rest()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "storage_" + Guid.CreateVersion7());
        var storagePath = Path.Combine(root, "files", "app");
        var noBackupStoragePath = Path.Combine(root, "no_backup", "app");
        WriteFile(storagePath, "account/portalSession.json", "session");
        WriteFile(storagePath, "app.log", "log");
        WriteFile(storagePath, "assets/ui/version/marker", "ui");
        WriteFile(storagePath, "Temp/SPA/file", "temp");
        WriteFile(storagePath, "settings.json", "settings");
        WriteFile(storagePath, "profiles/vpn_profiles.json", "access keys and codes");

        AppStorageMigration.MoveToNoBackup(storagePath, noBackupStoragePath);

        Assert.AreEqual("session", ReadFile(noBackupStoragePath, "account/portalSession.json"));
        Assert.AreEqual("log", ReadFile(noBackupStoragePath, "app.log"));
        Assert.AreEqual("ui", ReadFile(noBackupStoragePath, "assets/ui/version/marker"));
        Assert.AreEqual("temp", ReadFile(noBackupStoragePath, "Temp/SPA/file"));
        foreach (var name in new[] { "account", "app.log", "assets", "Temp" })
            Assert.IsFalse(Path.Exists(Path.Combine(storagePath, name)), $"{name} is still in the storage.");

        // the settings and the profiles, with the access keys and codes, stay as they were, to travel
        Assert.AreEqual("settings", ReadFile(storagePath, "settings.json"));
        Assert.AreEqual("access keys and codes", ReadFile(storagePath, "profiles/vpn_profiles.json"));
    }

    [TestMethod]
    [Obsolete("Migration (2026-10): remove with VpnHoodApp.MigrateInstallId.")]
    public async Task An_update_moves_the_install_id_out_of_the_settings()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "install_id_" + Guid.CreateVersion7());
        var storagePath = Path.Combine(root, "files", "app");
        // an install from before this version: its id in the settings, no device-only folder
        var installId = Guid.NewGuid().ToString();
        var settingsFilePath = Path.Combine(storagePath, "settings.json");
        WriteFile(storagePath, "settings.json", new JsonObject { ["ClientId"] = installId }.ToJsonString());

        // the update keeps the id, and the settings no longer carry it
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: storagePath,
            noBackupStoragePath: Path.Combine(root, "no_backup", "app"));
        await using (var app = TestAppHelper.CreateClientApp(appOptions))
            Assert.AreEqual(AppUtils.CreateClientId(appOptions.AppId, installId), app.Features.ClientId);
        Assert.IsFalse(ReadSettings(settingsFilePath).ContainsKey("ClientId"));
    }

    [TestMethod]
    public async Task A_device_restored_from_a_backup_makes_its_own_install_id()
    {
        var root = Path.Combine(TestAppHelper.WorkingPath, "install_id_" + Guid.CreateVersion7());
        var storagePath = Path.Combine(root, "files", "app");
        var clientId = await RunAppAndSaveSettings(storagePath, Path.Combine(root, "no_backup", "app"));
        var settingsFilePath = Path.Combine(storagePath, "settings.json");
        Assert.IsFalse(ReadSettings(settingsFilePath).ContainsKey("ClientId"), "The settings travel, so they carry no id.");

        // a device restored from a backup gets the storage back but not the device-only folder
        var restoredStoragePath = Path.Combine(root, "restored", "files", "app");
        Directory.CreateDirectory(restoredStoragePath);
        File.Copy(settingsFilePath, Path.Combine(restoredStoragePath, "settings.json"));
        var restoredClientId = await RunAppAndSaveSettings(restoredStoragePath,
            Path.Combine(root, "restored", "no_backup", "app"));
        Assert.AreNotEqual(clientId, restoredClientId);
    }

    // the app's client id, with its settings saved as a person's first change saves them
    private async Task<string> RunAppAndSaveSettings(string storagePath, string noBackupStoragePath)
    {
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: storagePath,
            noBackupStoragePath: noBackupStoragePath);
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        app.Settings.Save();
        return app.Features.ClientId;
    }

    private static JsonObject ReadSettings(string settingsFilePath)
    {
        return JsonNode.Parse(File.ReadAllText(settingsFilePath))?.AsObject() ??
               throw new InvalidOperationException("The settings file is empty.");
    }

    private static void WriteFile(string folderPath, string relativePath, string content)
    {
        var filePath = Path.Combine(folderPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ??
                                  throw new InvalidOperationException("A file path has no folder."));
        File.WriteAllText(filePath, content);
    }

    private static string ReadFile(string folderPath, string relativePath)
    {
        return File.ReadAllText(Path.Combine(folderPath, relativePath));
    }
}
