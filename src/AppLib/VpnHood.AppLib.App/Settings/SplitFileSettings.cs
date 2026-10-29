namespace VpnHood.AppLib.App.Settings;

// File-backed list settings core: one text file per list inside the folder. A missing file is
// created empty on FIRST access (read or signature) — so a stat-only change signature taken before a
// build always describes the files the build actually reads. Writes validate first: an unparsable
// list never reaches disk.
public abstract class SplitFileSettings(string folderPath)
{
    // One access at a time: a reconfigure reads the lists, connected or not, while the person may be
    // saving them, and a read and a write that meet on the open file fail one of them.
    private readonly Lock _fileLock = new();

    // throw when the content is not a valid list for this settings flavor
    protected abstract void Validate(string content);

    protected string GetFilePath(string fileTitle)
    {
        lock (_fileLock)
            return EnsureFile(fileTitle);
    }

    protected string Read(string fileTitle)
    {
        lock (_fileLock)
            return File.ReadAllText(EnsureFile(fileTitle));
    }

    protected void Write(string fileTitle, string content)
    {
        Validate(content);
        lock (_fileLock)
            File.WriteAllText(EnsureFile(fileTitle), content);
    }

    private string EnsureFile(string fileTitle)
    {
        var filePath = Path.Combine(folderPath, fileTitle + ".txt");
        if (File.Exists(filePath))
            return filePath;

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, string.Empty);
        return filePath;
    }
}
