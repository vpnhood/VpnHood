using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The VPN service's two folders (IDevice): the config folder, on this device only, and the data
// folder, which travels with a backup and holds only the person's proxies.
public static class VpnServiceFolders
{
    private const string ProxiesFolderName = "proxies";

    // the proxies the app and the VPN service share: in the data folder, so they travel
    public static string GetProxyStoreFilePath(string dataFolder)
    {
        return Path.Combine(dataFolder, ProxiesFolderName, "proxies.db");
    }

    // The VPN service kept its own files beside the proxies, in a folder that travels with a backup;
    // they move to the config folder once. Either process may come here first - always-on starts the
    // service before the app - so both do.
    [Obsolete("Migration (2026-10): drop a few months after it ships, with its calls in VpnServiceManager " +
              "and VpnServiceHost and VpnServiceFoldersTest.")]
    public static void MigrateServiceFiles(string dataFolder, string configFolder)
    {
        if (dataFolder == configFolder || !Directory.Exists(dataFolder))
            return;

        Directory.CreateDirectory(configFolder);
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(dataFolder)) {
            var name = Path.GetFileName(sourcePath);
            if (name == ProxiesFolderName)
                continue;

            try {
                if (FileUtils.MoveOrDiscard(sourcePath, Path.Combine(configFolder, name)))
                    VhLogger.Instance.LogInformation("A VPN service file has moved to its config folder. Name: {Name}", name);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not move a VPN service file to its config folder. Name: {Name}", name);
            }
        }
    }
}
