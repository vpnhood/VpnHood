using System.IO.Compression;
using System.Security.Cryptography;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Assets;

// The entries of a zip, as assets: extracted once into <extractFolderPath>/<version>/ and read from
// there as files. The version is the first sixteen hex digits of SHA-256 over the zip, so the bytes
// name the copy and nothing has to travel beside it; the price is one pass over the zip before the
// first read.
//
// The folder is this provider's, and no other zip may be given it: everything in it but the version
// in hand is deleted.
//
// One instance extracts once. A second cannot break the first: each copy is written to a folder of
// its own and moved into place, a version already there is taken as it stands, and the marker file
// is written before the move, so a folder carrying it is a folder that was finished.
public class ZipAssetProvider(IAsset zip, string extractFolderPath) : IAssetProvider
{
    private const string MarkerFileName = ".version";
    private readonly SemaphoreSlim _extractLock = new(1, 1);
    private FolderAssetProvider? _folder;

    public async Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        var folder = _folder ?? await Extract(cancellationToken).Vhc();
        return await folder.OpenReadAsync(assetPath, cancellationToken).Vhc();
    }

    private async Task<FolderAssetProvider> Extract(CancellationToken cancellationToken)
    {
        await _extractLock.WaitAsync(cancellationToken).Vhc();
        try {
            if (_folder != null)
                return _folder;

            // Hashed from its own read, going forward and holding nothing: this is the whole of a
            // start that finds the version already extracted, which is every start but the first
            // after an update.
            string version;
            await using (var zipStream = await zip.OpenReadAsync(cancellationToken).Vhc())
                version = Convert.ToHexStringLower(
                    (await SHA256.HashDataAsync(zipStream, cancellationToken).Vhc()).AsSpan(0, 8));

            var folderPath = Path.Combine(extractFolderPath, version);
            if (!IsExtracted(folderPath)) {
                // Opened again rather than kept from the hash, so nothing is held for the common
                // path. An archive reads its directory from the end, so this read must rewind: a
                // placed file does it where it lies, and only a stream that cannot - an Android
                // asset, an HTTP body - is bought into memory, which ZipArchive would otherwise do
                // itself, synchronously.
                var zipStream = await zip.OpenReadAsync(cancellationToken).Vhc();
                await using var seekable = await zipStream.ToMemoryStreamIfNotSeekableAsync(cancellationToken).Vhc();

                // A folder of this copy's own, so two extractions running at once - a second instance,
                // a second process - write to different places and neither sees the other's half-copy.
                var tempPath = Path.Combine(extractFolderPath, $"{version}.{Guid.NewGuid():N}.tmp");
                using (var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: true))
                    archive.ExtractToDirectory(tempPath, overwriteFiles: true);

                // last, so a folder that has it is a folder that was finished
                await File.WriteAllTextAsync(Path.Combine(tempPath, MarkerFileName), version, cancellationToken).Vhc();

                // The other one got there first: its copy is this copy, since the name is the bytes.
                if (IsExtracted(folderPath))
                    VhUtils.TryInvoke("Delete the extra assets copy", () => Directory.Delete(tempPath, true));
                else
                    Directory.Move(tempPath, folderPath);
            }

            // What is left of older versions, and of copies that never finished. After the move, not
            // before: a reader of this version must never lose the folder under it.
            DeleteOthers(extractFolderPath, version);

            _folder = new FolderAssetProvider(folderPath);
            return _folder;
        }
        finally {
            _extractLock.Release();
        }
    }

    private static bool IsExtracted(string folderPath)
    {
        return File.Exists(Path.Combine(folderPath, MarkerFileName));
    }

    private static void DeleteOthers(string extractFolderPath, string version)
    {
        if (!Directory.Exists(extractFolderPath))
            return;

        foreach (var path in Directory.EnumerateDirectories(extractFolderPath)) {
            if (Path.GetFileName(path) == version)
                continue;

            // one that is being written by someone else right now simply refuses, and goes next time
            VhUtils.TryInvoke("Delete an old assets copy", () => Directory.Delete(path, true));
        }
    }
}
