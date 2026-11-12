using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios.Utils;

/// <summary>
/// Provides a folder marked to stay on this iPhone: it carries the do-not-back-up flag, which asks iOS to leave it, and
/// all in it, out of iCloud and computer backups and a transfer to a new iPhone.
/// </summary>
public static class IosNoBackupFolder
{
    /// <summary>
    /// Makes a folder where it is missing, and sets its do-not-back-up flag, as a folder made anew has lost it; a flag
    /// that cannot be set is logged.
    /// </summary>
    /// <param name="folderPath">The folder's path.</param>
    public static void Create(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        using var url = NSUrl.FromFilename(folderPath);
        if (!url.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var error))
            VhLogger.Instance.LogError("Could not keep a folder out of the backups. Path: {Path}, Error: {Error}",
                folderPath, error.LocalizedDescription);
    }
}
