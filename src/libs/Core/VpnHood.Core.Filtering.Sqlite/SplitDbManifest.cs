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
/// <summary>
/// Provides the manifest of a filter folder: the list of the split databases active in it, in manifest order, which
/// an IP chain asks in turn and a domain chain the other way round.
/// </summary>
/// <remarks>
/// The app writes it after building the databases, and the VPN service's filter chains read it
/// (<see cref="SqliteIpFilterChain"/>, <see cref="SqliteDomainFilterChain"/>): a database file in the folder means
/// nothing unless the manifest lists it. A write also deletes the folder's database files it does not list.
/// </remarks>
public static class SplitDbManifest
{
    /// <summary>The name of the folder of split IP databases, under the VPN service's configuration folder.</summary>
    // the filter folders under IDevice.VpnServiceConfigFolder — the one location both processes read
    public const string IpFiltersFolderName = "ip-filters";

    /// <summary>The name of the folder of split domain databases, under the VPN service's configuration folder.</summary>
    public const string DomainFiltersFolderName = "domain-filters";
    private const string ManifestFileName = "manifest.json";

    // the service reads it as it starts while the app may be replacing it: the read and the rename that
    // meet each wait the other out
    private static readonly TimeSpan SharingTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Reads a folder's manifest.</summary>
    /// <param name="folderPath">The folder.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the paths of the databases it lists, each the folder as given joined with a file name,
    /// in the order the writer gave them; none when the folder has no manifest. An IP chain asks the databases in that
    /// order, a domain chain the other way round.
    /// </returns>
    /// <remarks>While the manifest is being replaced, it tries again for up to 2 seconds.</remarks>
    /// <exception cref="System.Text.Json.JsonException">The manifest is not valid JSON.</exception>
    /// <exception cref="InvalidDataException">The manifest is the JSON <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Writes a folder's manifest, replacing the old one whole, then deletes the folder's database files it does not
    /// list.
    /// </summary>
    /// <param name="folderPath">The folder, made if missing.</param>
    /// <param name="dbPaths">The databases, in manifest order, each directly in the folder.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// The manifest keeps file names only, so a reader in another process finds them in its own view of the folder.
    /// Each file whose name holds <c>.db</c> and does not start with a listed file's name is deleted where it can be;
    /// one that cannot be, as a file open elsewhere on Windows, stays until a later write. A listed database's
    /// <c>-wal</c>, <c>-shm</c> and <c>.tmp</c> files start with its name, and stay.
    /// </remarks>
    /// <exception cref="ArgumentException">A database is not directly in the folder.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
