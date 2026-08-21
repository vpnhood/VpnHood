using System.Security.Cryptography;
using System.Text;

namespace VpnHood.AppLib.Utils;

public static class AppUtils
{
    // Mac Catalyst is checked BEFORE iOS on purpose: OperatingSystem.IsIOS() reports true for Catalyst
    // too, so testing iOS first would label a Mac build as an iPhone and hide/show the wrong content.
    public static AppOsType GetOsType()
    {
        if (OperatingSystem.IsAndroid()) return AppOsType.Android;
        if (OperatingSystem.IsMacCatalyst()) return AppOsType.MacOs;
        if (OperatingSystem.IsIOS()) return AppOsType.Ios;
        if (OperatingSystem.IsWindows()) return AppOsType.Windows;
        if (OperatingSystem.IsMacOS()) return AppOsType.MacOs;
        if (OperatingSystem.IsLinux()) return AppOsType.Linux;
        return AppOsType.Unknown;
    }

    // A stable per-install id: MD5 over "appId:deviceId", read as a Guid. MD5 is not used as a
    // security primitive here — it is the shape that keeps existing installs on the id they already
    // report, so changing it would orphan every device's history.
    public static string CreateClientId(string appId, string deviceId)
    {
        // Convert the combined string to bytes
        var uid = $"{appId}:{deviceId}";
        var uiBytes = Encoding.UTF8.GetBytes(uid);

        // Create an MD5 instance and compute the hash
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(uiBytes);

        // convert to Guid for compatibility
        var guid = new Guid(hashBytes);
        return guid.ToString();
    }

    // Cheap change signature over files (modified time + length; no content read) — one label:ticks:length
    // entry per file, a missing file counts as "none". Callers store it (e.g. as a split db's
    // source_signature meta) to detect stale derived data without parsing the sources.
    // Kept human-readable on purpose: the stored signature then shows exactly which file it was built
    // from, so a did-it-rebuild question is answered by inspection.
    public static string BuildFileSignature(params string[] filePaths)
    {
        return string.Join(',', filePaths.Select(filePath => {
            var fileInfo = new FileInfo(filePath);
            var fileTitle = Path.GetFileNameWithoutExtension(filePath);
            return fileInfo.Exists
                ? $"{fileTitle}:{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}"
                : $"{fileTitle}:none";
        }));
    }
}
