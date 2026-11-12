using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Provides the VPN service's two folders (<c>IDevice</c>): the config folder, on this device only, and the data
/// folder, which travels with a backup and holds only the person's proxies.
/// </summary>
/// <remarks>On a desktop, where nothing backs up the app's data, the two are one folder.</remarks>
public static class VpnServiceFolders
{
    private const string ProxiesFolderName = "proxies";

    /// <summary>
    /// Gets the path of the store of the proxies the app and the VPN service share, in the data folder, so they travel.
    /// </summary>
    /// <param name="dataFolder">The data folder.</param>
    /// <returns>The store's path: <c>proxies/proxies.db</c> under <paramref name="dataFolder"/>.</returns>
    public static string GetProxyStoreFilePath(string dataFolder)
    {
        return Path.Combine(dataFolder, ProxiesFolderName, "proxies.db");
    }

    /// <summary>
    /// Moves the VPN service's own files, all but the proxies, from the data folder, where it kept them, to the config
    /// folder, once.
    /// </summary>
    /// <param name="dataFolder">The data folder, which travels with a backup.</param>
    /// <param name="configFolder">The config folder, on this device only.</param>
    /// <remarks>
    /// Either process may come here first — always-on starts the service before the app — so both call it. A file the
    /// config folder has already is deleted rather than moved, and one that cannot go is logged and left.
    /// </remarks>
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
