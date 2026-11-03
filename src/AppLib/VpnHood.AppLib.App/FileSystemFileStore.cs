using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App;

// The app's files as plain files, where the platform keeps them (IAppFileStore).
public class FileSystemFileStore : IAppFileStore
{
    public string? Read(string filePath)
    {
        try {
            return File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return null;
        }
    }

    public void Write(string filePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ??
                                  throw new ArgumentException("The file has no folder.", nameof(filePath)));
        File.WriteAllText(filePath, content);
    }

    public void Delete(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public string GetSignature(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return fileInfo.Exists ? $"{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}" : "none";
    }
}
