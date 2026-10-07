using System.Text.Json.Nodes;
using VpnHood.AppLib.App.Windows;

namespace VpnHood.AppLib.Test.Tests;

// The import of the releases before the Windows service, with the machine's profile list and group
// check stood in for.
[TestClass]
public class LegacyStorageTest : TestAppBase
{
    private const string LegacyFolderName = "VpnHoodTest";
    private const string AdminSid = "S-1-5-21-1-2-3-1001";
    private const string UserSid = "S-1-5-21-1-2-3-1002";
    private const string UnreadableSid = "S-1-5-21-1-2-3-1003";

    private static bool IsAdministrator(string sid)
    {
        return sid == UnreadableSid
            ? throw new InvalidOperationException("The groups could not be read.")
            : sid == AdminSid;
    }

    [TestMethod]
    public void An_administrators_folder_is_taken_and_no_other()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only: the earlier releases' paths are Windows'.");

        // the administrator's folder is the oldest of the three, so neither newer one may win
        var root = Path.Combine(TestAppHelper.WorkingPath, "legacy_" + Guid.CreateVersion7());
        var now = DateTime.UtcNow;
        var admin = CreateProfile(root, "admin", now.AddHours(-2));
        var user = CreateProfile(root, "user", now.AddHours(-1));
        var unreadable = CreateProfile(root, "unreadable", now);
        var storagePath = Path.Combine(root, "storage");

        LegacyStorage.Import(storagePath, LegacyFolderName,
            [(UserSid, user), (UnreadableSid, unreadable), (AdminSid, admin)], IsAdministrator);

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(storagePath, "settings.json")));
        Assert.IsNotNull(settings);
        Assert.AreEqual(AdminSid, settings["ClientId"]?.GetValue<string>());
        Assert.AreEqual("admin", File.ReadAllText(Path.Combine(storagePath, "profiles", "vpn_profiles.json")));

        // the sign-in comes along, so a signed-in person stays signed in
        Assert.AreEqual("admin", File.ReadAllText(Path.Combine(storagePath, "account", "portalSession.json")));
        Assert.AreEqual("admin", File.ReadAllText(Path.Combine(storagePath, "account", "account.json")));

        // a person's debug commands stay behind; their other settings come along
        var userSettings = settings["UserSettings"]?.AsObject();
        Assert.IsNotNull(userSettings);
        Assert.IsFalse(userSettings.ContainsKey("DebugData1"));
        Assert.IsFalse(userSettings.ContainsKey("DebugData2"));
        Assert.AreEqual("fr", userSettings["CultureCode"]?.GetValue<string>());
    }

    [TestMethod]
    public void Nothing_is_taken_without_an_administrators_folder()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only: the earlier releases' paths are Windows'.");

        var root = Path.Combine(TestAppHelper.WorkingPath, "legacy_" + Guid.CreateVersion7());
        var user = CreateProfile(root, "user", DateTime.UtcNow);
        var unreadable = CreateProfile(root, "unreadable", DateTime.UtcNow);
        var storagePath = Path.Combine(root, "storage");

        LegacyStorage.Import(storagePath, LegacyFolderName, [(UserSid, user), (UnreadableSid, unreadable)],
            IsAdministrator);

        Assert.IsFalse(Directory.Exists(storagePath));
    }

    // A profile folder with an earlier release's files; its profiles and account files hold the
    // profile's name.
    private static string CreateProfile(string root, string name, DateTime settingsTime)
    {
        var profilePath = Path.Combine(root, name);
        var folderPath = Path.Combine(profilePath, "AppData", "Local", LegacyFolderName);
        Directory.CreateDirectory(Path.Combine(folderPath, "profiles"));
        File.WriteAllText(Path.Combine(folderPath, "profiles", "vpn_profiles.json"), name);
        Directory.CreateDirectory(Path.Combine(folderPath, "account"));
        File.WriteAllText(Path.Combine(folderPath, "account", "portalSession.json"), name);
        File.WriteAllText(Path.Combine(folderPath, "account", "account.json"), name);

        var settingsFilePath = Path.Combine(folderPath, "settings.json");
        File.WriteAllText(settingsFilePath,
            """{ "ClientId": "old", "UserSettings": { "CultureCode": "fr", "DebugData1": "/remote-access", "DebugData2": "x" } }""");
        File.SetLastWriteTimeUtc(settingsFilePath, settingsTime);
        return profilePath;
    }
}
