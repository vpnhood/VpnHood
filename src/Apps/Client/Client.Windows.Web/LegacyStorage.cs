using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Client.Windows.Web;

// Migration (2026-09): drop a few months after it ships.
// The releases before the service kept the app's files in the person's own folder. The service's first
// start copies the newest such folder's files in, with its person's SID as the client id those releases
// went by. Only the known files, none reached through a link: the service is LocalSystem and the
// folder is the person's to shape. settings.json goes last, so a start cut short copies again.
internal static class LegacyStorage
{
    private const string SettingsFileName = "settings.json";

    private static readonly string[] FileNames = [
        @"profiles\vpn_profiles.json",
        @"splits\ips_via_app\includes.txt", @"splits\ips_via_app\excludes.txt", @"splits\ips_via_app\blocks.txt",
        @"splits\ips_via_device\includes.txt", @"splits\ips_via_device\excludes.txt",
        @"splits\domains\includes.txt", @"splits\domains\excludes.txt", @"splits\domains\blocks.txt"
    ];

    public static void TryImport(string storagePath, string legacyFolderName)
    {
        try {
            using var identity = WindowsIdentity.GetCurrent();
            var settingsFilePath = Path.Combine(storagePath, SettingsFileName);
            var folderName = Path.Combine("AppData", "Local", legacyFolderName);
            if (!identity.IsSystem || File.Exists(settingsFilePath) || FindNewest(folderName) is not { } legacy)
                return;

            foreach (var fileName in FileNames) {
                if (GetPlainFile(legacy.ProfilePath, Path.Combine(folderName, fileName)) is not { } sourcePath)
                    continue;

                var filePath = Path.Combine(storagePath, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ??
                                          throw new InvalidOperationException($"The file has no folder: {filePath}"));
                File.WriteAllBytes(filePath, File.ReadAllBytes(sourcePath));
            }

            var settings = JsonNode.Parse(File.ReadAllText(legacy.SettingsFilePath)) ??
                           throw new InvalidOperationException("The earlier release's settings are empty.");
            settings["ClientId"] = legacy.Sid;
            File.WriteAllText(settingsFilePath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            VhLogger.Instance.LogInformation("Copied the settings of an earlier release from {ProfilePath}.", legacy.ProfilePath);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not copy the settings of an earlier release, so the app starts without them.");
        }
    }

    // The person whose folder changed last. Windows' profile list pairs each profile folder with its SID.
    private static (string ProfilePath, string Sid, string SettingsFilePath)? FindNewest(string folderName)
    {
        using var profileList = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profileList == null)
            return null;

        (string ProfilePath, string Sid, string SettingsFilePath)? newest = null;
        foreach (var sid in profileList.GetSubKeyNames()) {
            using var profile = profileList.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is not string profilePath ||
                GetPlainFile(profilePath, Path.Combine(folderName, SettingsFileName)) is not { } settingsFilePath)
                continue;

            if (newest is not { } current ||
                File.GetLastWriteTimeUtc(settingsFilePath) > File.GetLastWriteTimeUtc(current.SettingsFilePath))
                newest = (profilePath, sid, settingsFilePath);
        }

        return newest;
    }

    // The file, or null when it is missing or any folder on its way is a junction or a link.
    private static string? GetPlainFile(string profilePath, string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        var path = profilePath;
        for (var i = 0; i < parts.Length; i++) {
            path = Path.Combine(path, parts[i]);
            FileSystemInfo info = i < parts.Length - 1 ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return null;
        }

        return path;
    }
}
