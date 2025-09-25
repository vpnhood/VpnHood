using System.Collections.Frozen;
// ReSharper disable StringLiteralTypo

namespace VpnHood.AppLib.WebServer.Helpers;

/// <summary>
/// Helper class for determining MIME content types based on file extensions
/// </summary>
public static class MimeTypeHelper
{
    private static readonly FrozenDictionary<string, string> MimeTypes = new Dictionary<string, string>
    {
        // Text files
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".mjs"] = "application/javascript",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".rtf"] = "application/rtf",
        [".md"] = "text/markdown",
        [".yaml"] = "text/yaml",
        [".yml"] = "text/yaml",

        // Images
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".webp"] = "image/webp",
        [".tiff"] = "image/tiff",
        [".tif"] = "image/tiff",
        [".avif"] = "image/avif",

        // Audio
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",
        [".wma"] = "audio/x-ms-wma",

        // Video
        [".mp4"] = "video/mp4",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".flv"] = "video/x-flv",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".m4v"] = "video/mp4",
        [".3gp"] = "video/3gpp",

        // Documents
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".odp"] = "application/vnd.oasis.opendocument.presentation",

        // Archives
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".bz2"] = "application/x-bzip2",

        // Fonts
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".eot"] = "application/vnd.ms-fontobject",

        // Application files
        [".exe"] = "application/vnd.microsoft.portable-executable",
        [".msi"] = "application/x-msdownload",
        [".dmg"] = "application/x-apple-diskimage",
        [".deb"] = "application/vnd.debian.binary-package",
        [".rpm"] = "application/x-rpm",
        [".apk"] = "application/vnd.android.package-archive",

        // Web related
        [".wasm"] = "application/wasm",
        [".map"] = "application/json",
        [".manifest"] = "text/cache-manifest",
        [".webmanifest"] = "application/manifest+json",

        // Data formats
        [".sqlite"] = "application/vnd.sqlite3",
        [".db"] = "application/x-sqlite3",
        [".sql"] = "application/sql",

        // Programming files
        [".cs"] = "text/plain",
        [".java"] = "text/plain",
        [".py"] = "text/plain",
        [".cpp"] = "text/plain",
        [".c"] = "text/plain",
        [".h"] = "text/plain",
        [".php"] = "text/plain",
        [".rb"] = "text/plain",
        [".go"] = "text/plain",
        [".rs"] = "text/plain",
        [".ts"] = "text/plain",
        [".jsx"] = "text/plain",
        [".tsx"] = "text/plain",
        [".vue"] = "text/plain",
        [".swift"] = "text/plain",
        [".kt"] = "text/plain",
        [".dart"] = "text/plain",

        // Configuration files
        [".conf"] = "text/plain",
        [".ini"] = "text/plain",
        [".cfg"] = "text/plain",
        [".toml"] = "text/plain",
        [".properties"] = "text/plain",

        // Log files
        [".log"] = "text/plain",
        [".out"] = "text/plain",
        [".err"] = "text/plain"

    }.ToFrozenDictionary();

    /// <summary>
    /// Gets the MIME content type for a given file path based on its extension
    /// </summary>
    /// <param name="path">The file path</param>
    /// <returns>The MIME content type, or "application/octet-stream" if unknown</returns>
    public static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return MimeTypes.GetValueOrDefault(extension, "application/octet-stream");
    }

    /// <summary>
    /// Checks if the given extension is supported
    /// </summary>
    /// <param name="extension">The file extension (including the dot)</param>
    /// <returns>True if the extension is supported, false otherwise</returns>
    public static bool IsSupported(string extension)
    {
        return MimeTypes.ContainsKey(extension.ToLowerInvariant());
    }

    /// <summary>
    /// Gets all supported extensions
    /// </summary>
    /// <returns>A collection of all supported file extensions</returns>
    public static IEnumerable<string> GetSupportedExtensions()
    {
        return MimeTypes.Keys;
    }
}