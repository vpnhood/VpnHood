using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Test.Tests;

// The VPN service's own files leave the folder that travels with a backup for its device-only config
// folder, once, whichever process comes first; the person's proxies stay to travel.
[TestClass]
[Obsolete("Migration (2026-10): remove with VpnServiceFolders.MigrateServiceFiles.")]
public class VpnServiceFoldersTest : TestBase
{
    [TestMethod]
    public void The_service_files_move_and_the_proxies_stay()
    {
        var root = Path.Combine(TestHelper.WorkingPath, "vpn_folders_" + Guid.CreateVersion7());
        var dataFolder = Path.Combine(root, "files", "vpn-service");
        var configFolder = Path.Combine(root, "no_backup", "vpn-service");
        WriteFile(dataFolder, ClientOptions.VpnConfigFileName, "config");
        WriteFile(dataFolder, ClientOptions.VpnStatusFileName, "status");
        WriteFile(dataFolder, ClientOptions.VpnLogFileName, "log");
        WriteFile(dataFolder, Path.Combine("ip-filters", "manifest"), "filters");
        var proxyStoreFilePath = VpnServiceFolders.GetProxyStoreFilePath(dataFolder);
        WriteFile(dataFolder, Path.GetRelativePath(dataFolder, proxyStoreFilePath), "proxies");

        // the app and the VPN service each do it; the second finds nothing left
        VpnServiceFolders.MigrateServiceFiles(dataFolder, configFolder);
        VpnServiceFolders.MigrateServiceFiles(dataFolder, configFolder);

        Assert.AreEqual("config", ReadFile(configFolder, ClientOptions.VpnConfigFileName));
        Assert.AreEqual("status", ReadFile(configFolder, ClientOptions.VpnStatusFileName));
        Assert.AreEqual("log", ReadFile(configFolder, ClientOptions.VpnLogFileName));
        Assert.AreEqual("filters", ReadFile(configFolder, Path.Combine("ip-filters", "manifest")));
        Assert.AreEqual("proxies", File.ReadAllText(proxyStoreFilePath));
        Assert.HasCount(1, Directory.GetFileSystemEntries(dataFolder), "Only the proxies stay to travel.");
    }

    [TestMethod]
    public void A_file_already_in_the_config_folder_is_the_one_kept()
    {
        var root = Path.Combine(TestHelper.WorkingPath, "vpn_folders_" + Guid.CreateVersion7());
        var dataFolder = Path.Combine(root, "files", "vpn-service");
        var configFolder = Path.Combine(root, "no_backup", "vpn-service");
        WriteFile(dataFolder, ClientOptions.VpnStatusFileName, "old");
        WriteFile(configFolder, ClientOptions.VpnStatusFileName, "current");

        VpnServiceFolders.MigrateServiceFiles(dataFolder, configFolder);

        Assert.AreEqual("current", ReadFile(configFolder, ClientOptions.VpnStatusFileName));
        Assert.IsFalse(File.Exists(Path.Combine(dataFolder, ClientOptions.VpnStatusFileName)));
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
