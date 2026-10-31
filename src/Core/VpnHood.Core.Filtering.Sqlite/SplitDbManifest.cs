using System.Text.Json;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// The source of truth for WHICH split dbs are currently active in a filter folder. The app process
// writes it atomically after building the dbs; the service's filter chains read it through their paths
// provider — so nothing travels through vpn.config or the reconfigure request, and a stale db file
// lying in the folder means nothing (presence on disk is never policy, the manifest is).
// Writing also sweeps: this class owns the folder's db inventory, so any db-family file it does not
// list is superseded and gets deleted on a best-effort basis — a file still open elsewhere survives
// the sweep and is retried on the next write.
public static class SplitDbManifest
{
    // the filter folders under IDevice.VpnServiceConfigFolder — the one location both processes read
    public const string IpFiltersFolderName = "ip-filters";
    public const string DomainFiltersFolderName = "domain-filters";
    private const string ManifestFileName = "manifest.json";

    // the service reads it as it starts while the app may be replacing it: the read and the rename that
    // meet each wait the other out
    private static readonly TimeSpan SharingTimeout = TimeSpan.FromSeconds(2);

    // Absolute db paths in gate order; a missing manifest means "no splits configured" — fail-closed:
    // no gates means everything tunnels, nothing leaks around. A corrupt manifest throws (fail loud).
    public static async Task<string[]> Read(string folderPath, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(folderPath, ManifestFileName);
        if (!File.Exists(manifestPath))
            return [];

        var json = await FileUtils.WaitOutSharingAsync(() => File.ReadAllTextAsync(manifestPath, cancellationToken),
            SharingTimeout, cancellationToken).Vhc();
        var data = JsonSerializer.Deserialize<ManifestData>(json)
                   ?? throw new InvalidDataException($"Could not deserialize {manifestPath}.");
        return data.DbFiles.Select(fileName => Path.Combine(folderPath, fileName)).ToArray();
    }

    // Publish the current db set (replaced whole, so a concurrent Read never sees a torn file, over a temp
    // file of its own, so two writers never meet on one), then sweep superseded db-family files. Every path
    // must live inside folderPath: the manifest names files, not locations, so a Read from either process
    // resolves against its own folder view.
    public static async Task Write(string folderPath, string[] dbPaths, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var dbFileNames = dbPaths.Select(dbPath => GetFileNameInFolder(folderPath, dbPath)).ToArray();

        var manifestPath = Path.Combine(folderPath, ManifestFileName);
        var json = JsonSerializer.Serialize(new ManifestData { DbFiles = dbFileNames });
        await FileUtils.WaitOutSharingAsync(
            () => FileUtils.WriteAllTextAtomicAsync(manifestPath, json, cancellationToken),
            SharingTimeout, cancellationToken).Vhc();

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
        public string[] DbFiles { get; init; } = [];
    }
}
