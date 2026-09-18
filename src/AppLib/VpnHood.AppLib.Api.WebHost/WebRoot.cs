using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Api.WebHost;

// The UI's files on disk, under Temp/WebRoot/<hash of its zip>: extracted once per version and shared
// by every host the factory makes. It sits outside the hosts on purpose - two of them extracting into
// one folder would race, one deleting what the other is unpacking - and unpacks on first read, so a
// head that never serves a page never unpacks one. A folder whose index.html exists is complete.
internal class WebRoot(ReadOnlyMemory<byte> zip, string storageFolderPath)
{
    private readonly Lock _lock = new();
    private string? _folderPath;
    private string? _indexHtml;

    // Of the zip, so a web view holding an older build is never served it from its cache.
    public string Hash { get; } = Convert.ToHexString(MD5.HashData(zip.Span));

    public string FolderPath {
        get {
            lock (_lock)
                return _folderPath ??= Extract();
        }
    }

    // The one file every unmatched path falls back to, so the UI can route itself.
    public string IndexHtml {
        get {
            var folderPath = FolderPath;
            lock (_lock)
                return _indexHtml ??= File.ReadAllText(Path.Combine(folderPath, "index.html"));
        }
    }

    private string Extract()
    {
        var rootsFolderPath = Path.Combine(storageFolderPath, "Temp", "WebRoot");
        var folderPath = Path.Combine(rootsFolderPath, Hash);
        if (File.Exists(Path.Combine(folderPath, "index.html")))
            return folderPath;

        // an older version's folder goes on the way
        if (Directory.Exists(rootsFolderPath))
            VhUtils.TryInvoke("Delete old WebRoot folder", () => Directory.Delete(rootsFolderPath, true));

        using var zipArchive = new ZipArchive(OpenZipStream(zip));
        zipArchive.ExtractToDirectory(folderPath, true);
        return folderPath;
    }

    // The zip's own array when it has one; a copy only when it is some other memory.
    private static MemoryStream OpenZipStream(ReadOnlyMemory<byte> data)
    {
        return MemoryMarshal.TryGetArray(data, out var segment) && segment.Array != null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray());
    }
}
