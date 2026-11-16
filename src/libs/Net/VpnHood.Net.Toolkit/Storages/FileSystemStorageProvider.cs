using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Storages;

/// <summary>
/// Provides a storage of small text files as plain files.
/// </summary>
public class FileSystemStorageProvider : IStorageProvider
{
    /// <inheritdoc/>
    public string? Read(string filePath)
    {
        try {
            return File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It makes the file's folder when missing, and replaces the file atomically.</remarks>
    /// <exception cref="ArgumentException">The path has no folder.</exception>
    // Whole or not at all, as IStorageProvider.Write promises: File.WriteAllText empties the file first, so a full
    // disk would leave it cut.
    public void Write(string filePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ??
                                  throw new ArgumentException("The file has no folder.", nameof(filePath)));
        FileUtils.WriteAllTextAtomic(filePath, content);
    }

    /// <inheritdoc/>
    public void Delete(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    /// <inheritdoc/>
    /// <remarks>It is the file's last write time and length, or <c>none</c> when there is no such file.</remarks>
    public string GetSignature(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return fileInfo.Exists ? $"{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}" : "none";
    }
}
