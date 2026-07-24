using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// The source of truth for WHICH split dbs are currently active in a filter folder. The app process
// writes it atomically after building the dbs; the service's filter chains read it through their paths
// provider — so nothing travels through vpn.config or the reconfigure request, and a stale db file
// lying in the folder means nothing (presence on disk is never policy, the manifest is).
// Writing also sweeps: this class owns the folder's db inventory, so any db-family file it does not
// list is superseded and gets deleted (best effort — a file still open elsewhere survives and is
// retried on the next write).
public static class SplitDbManifest
{
    // the filter folders under IDevice.VpnServiceConfigFolder — the one location both processes read
    public const string IpFiltersFolderName = "ip-filters";
    public const string DomainFiltersFolderName = "domain-filters";
    private const string ManifestFileName = "manifest.json";

    // Absolute db paths in gate order; a missing manifest means "no splits configured" — fail-closed:
    // no gates means everything tunnels, nothing leaks around. A corrupt manifest throws (fail loud).
    public static string[] Read(string folderPath)
    {
        var manifestPath = Path.Combine(folderPath, ManifestFileName);
        if (!File.Exists(manifestPath))
            return [];

        var data = JsonSerializer.Deserialize<ManifestData>(File.ReadAllText(manifestPath))
                   ?? throw new InvalidDataException($"Could not deserialize {manifestPath}.");
        return data.DbFiles.Select(fileName => Path.Combine(folderPath, fileName)).ToArray();
    }

    // Publish the current db set (atomic temp+rename, so a concurrent Read never sees a torn file),
    // then sweep superseded db-family files. Every path must live inside folderPath: the manifest
    // names files, not locations, so a Read from either process resolves against its own folder view.
    public static void Write(string folderPath, string[] dbPaths)
    {
        Directory.CreateDirectory(folderPath);
        var dbFileNames = dbPaths.Select(dbPath => GetFileNameInFolder(folderPath, dbPath)).ToArray();

        var manifestPath = Path.Combine(folderPath, ManifestFileName);
        var tempPath = manifestPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new ManifestData { DbFiles = dbFileNames }));
        File.Move(tempPath, manifestPath, overwrite: true);

        // *.db* also catches sqlite side files (-wal/-shm) and orphaned build temps; the prefix match
        // keeps the side files of every listed db
        foreach (var filePath in Directory.GetFiles(folderPath, "*.db*")) {
            var fileName = Path.GetFileName(filePath);
            if (!dbFileNames.Any(dbFileName => fileName.StartsWith(dbFileName, StringComparison.OrdinalIgnoreCase)))
                VhUtils.TryDeleteFile(filePath);
        }
    }

    private static string GetFileNameInFolder(string folderPath, string dbPath)
    {
        var dbFolderPath = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.Equals(dbFolderPath, Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath)),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The db must live inside the manifest folder. Db: {dbPath}, Folder: {folderPath}");

        return Path.GetFileName(dbPath);
    }

    private sealed class ManifestData
    {
        public string[] DbFiles { get; set; } = [];
    }
}
