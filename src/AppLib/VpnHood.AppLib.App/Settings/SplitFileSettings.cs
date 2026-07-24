namespace VpnHood.AppLib.Settings;

// File-backed list settings core: one text file per list inside the folder. A missing file is
// created empty on FIRST access (read or signature) — so a stat-only change signature taken before a
// build always describes the files the build actually reads. Writes validate first: an unparsable
// list never reaches disk.
public abstract class SplitFileSettings(string folderPath)
{
    // throw when the content is not a valid list for this settings flavor
    protected abstract void Validate(string content);

    protected string GetFilePath(string fileTitle)
    {
        var filePath = Path.Combine(folderPath, fileTitle + ".txt");
        if (File.Exists(filePath))
            return filePath;

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, string.Empty);
        return filePath;
    }

    protected string Read(string fileTitle)
    {
        return File.ReadAllText(GetFilePath(fileTitle));
    }

    protected void Write(string fileTitle, string content)
    {
        Validate(content);
        File.WriteAllText(GetFilePath(fileTitle), content);
    }
}
