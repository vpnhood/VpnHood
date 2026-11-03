using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App.Settings;

// List settings core: one text file per list inside the folder, kept by the app's file store. A missing
// list reads as empty and signs as missing until its first write, so a change signature taken before a
// build describes what the build reads. Writes validate first: an unparsable list is never kept.
public abstract class SplitFileSettings(IAppFileStore fileStore, string folderPath)
{
    // One access at a time: a reconfigure reads the lists, connected or not, while the person may be
    // saving them, and a read and a write that meet on the open file fail one of them.
    private readonly Lock _fileLock = new();

    // throw when the content is not a valid list for this settings flavor
    protected abstract void Validate(string content);

    protected string Read(string fileTitle)
    {
        lock (_fileLock)
            return fileStore.Read(GetFilePath(fileTitle)) ?? string.Empty;
    }

    protected void Write(string fileTitle, string content)
    {
        Validate(content);
        lock (_fileLock)
            fileStore.Write(GetFilePath(fileTitle), content);
    }

    // The lists' change signature, stored as the db's source_signature meta so the db builders tell a
    // stale db without parsing the lists. Every setting write rewrites its list, so the signature always
    // changes with the content.
    protected string GetSignature(params string[] fileTitles)
    {
        lock (_fileLock)
            return string.Join(',', fileTitles.Select(fileTitle =>
                $"{fileTitle}:{fileStore.GetSignature(GetFilePath(fileTitle))}"));
    }

    private string GetFilePath(string fileTitle)
    {
        return Path.Combine(folderPath, fileTitle + ".txt");
    }
}
