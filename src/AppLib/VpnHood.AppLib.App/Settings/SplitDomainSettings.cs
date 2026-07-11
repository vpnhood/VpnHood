using VpnHood.AppLib.Utils;

namespace VpnHood.AppLib.Settings;

public class SplitDomainSettings(string folderPath)
{
    private string GetFilePath(string fileTitle)
    {
        var filePath = Path.Combine(folderPath, fileTitle + ".txt");
        if (File.Exists(filePath))
            return filePath;

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, string.Empty);
        return filePath;
    }

    public string Includes {
        get => Read("includes");
        set => Write("includes", value);
    }

    public string Excludes {
        get => Read("excludes");
        set => Write("excludes", value);
    }

    public string Blocks {
        get => Read("blocks");
        set => Write("blocks", value);
    }

    // Stat-only change signature of the split-domain sources, stored as the db's source_signature meta
    // so SplitDomainDbBuilder.EnsureAsync detects stale dbs without parsing the text files. Every setting
    // write rewrites its file, so the signature always changes with the content.
    public string GetSplitDomainSignature() => AppUtils.BuildFileSignature(
        Path.Combine(folderPath, "includes.txt"),
        Path.Combine(folderPath, "excludes.txt"),
        Path.Combine(folderPath, "blocks.txt"));

    private string Read(string fileTitle)
    {
        return File.ReadAllText(GetFilePath(fileTitle));
    }

    private void Write(string fileTitle, string content)
    {
        DomainTextFileParser.Parse(content); // validate
        File.WriteAllText(GetFilePath(fileTitle), content);
    }
}
