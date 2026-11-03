namespace VpnHood.Net.Toolkit.Storages;

// A folder of small text files that its provider keeps; OpenStorage gives a folder inside it. It takes
// names, never paths, so whoever holds one stays inside its folder.
public sealed class Storage(IStorageProvider provider, string folderPath)
{
    // for the plain files beside the provider's (a provider that keeps entries, not files, has nothing here)
    public string FolderPath { get; } = Path.GetFullPath(folderPath);

    public Storage OpenStorage(string name) => new(provider, GetPath(name));
    public string? Read(string name) => provider.Read(GetPath(name));
    public void Write(string name, string content) => provider.Write(GetPath(name), content);
    public void Delete(string name) => provider.Delete(GetPath(name));
    public string GetSignature(string name) => provider.GetSignature(GetPath(name));

    // ':' too: on Windows "C:" is rooted, and Path.Combine would drop the folder for it
    private string GetPath(string name)
    {
        if (name is "" or "." or ".." || name.AsSpan().IndexOfAny('/', '\\', ':') >= 0)
            throw new ArgumentException($"A storage takes a name, not a path. Name: {name}", nameof(name));

        return Path.Combine(FolderPath, name);
    }
}
