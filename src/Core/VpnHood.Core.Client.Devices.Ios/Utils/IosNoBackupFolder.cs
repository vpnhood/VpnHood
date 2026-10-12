using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios.Utils;

// A folder that stays on this iPhone: iOS leaves a folder carrying the do-not-back-up flag, and all in
// it, out of iCloud and computer backups and a transfer to a new iPhone. A folder made anew has lost
// the flag, so it is set at each start.
public static class IosNoBackupFolder
{
    public static void Create(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        using var url = NSUrl.FromFilename(folderPath);
        if (!url.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var error))
            VhLogger.Instance.LogError("Could not keep a folder out of the backups. Path: {Path}, Error: {Error}",
                folderPath, error?.LocalizedDescription);
    }
}
