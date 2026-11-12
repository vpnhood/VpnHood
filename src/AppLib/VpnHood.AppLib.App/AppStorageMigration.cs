using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Utils;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides the one move of what must not travel with a backup, the sign-in session and the account's cache, the
/// log, the unpacked UI and the temporary files, from the storage folder to the device-only one, which a platform
/// makes before the head's providers read them.
/// </summary>
[Obsolete("Migration (2026-10): drop a few months after it ships, with its calls in VpnHoodAndroidApp " +
          "and VpnHoodIosApp and the An_update_moves_what_must_not_travel_and_leaves_the_rest test.")]
public static class AppStorageMigration
{
    private static readonly string[] ItemNames = [
        VpnHoodApp.FolderNameAccount, VpnHoodApp.FileNameLog, AppUtils.FolderNameAssets, VpnHoodApp.FolderNameTemp
    ];

    /// <summary>
    /// Moves what must not travel with a backup to the device-only folder; an item that is there already stays, and the
    /// old copy goes. An item that fails to move is logged and left.
    /// </summary>
    /// <param name="storagePath">The storage folder (<see cref="AppOptionsContext.StoragePath"/>).</param>
    /// <param name="noBackupStoragePath">
    /// The device-only folder (<see cref="AppOptionsContext.NoBackupStoragePath"/>).
    /// </param>
    /// <exception cref="IOException">The device-only folder could not be made.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not make the device-only folder.</exception>
    public static void MoveToNoBackup(string storagePath, string noBackupStoragePath)
    {
        if (storagePath == noBackupStoragePath)
            return;

        Directory.CreateDirectory(noBackupStoragePath);
        foreach (var name in ItemNames) {
            try {
                if (FileUtils.MoveOrDiscard(Path.Combine(storagePath, name), Path.Combine(noBackupStoragePath, name)))
                    VhLogger.Instance.LogInformation("A file has moved to the device-only storage. Name: {Name}", name);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not move a file to the device-only storage. Name: {Name}", name);
            }
        }
    }
}
