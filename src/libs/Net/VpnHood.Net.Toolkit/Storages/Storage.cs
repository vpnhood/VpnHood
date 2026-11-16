namespace VpnHood.Net.Toolkit.Storages;

/// <summary>
/// Provides a folder of small text files that its provider keeps; <see cref="OpenStorage"/> gives a folder inside it.
/// It takes names, never paths, so whoever holds one stays inside its folder, links on the disk aside.
/// </summary>
/// <param name="provider">The provider that keeps the files.</param>
/// <param name="folderPath">The folder's path.</param>
// One class over a provider of full paths, rather than storages that wrap their parents: a wrapper hands its parent a
// path, so every level would take paths and check them; here the joining, the check and the nesting are written once,
// and a platform writes four methods.
public sealed class Storage(IStorageProvider provider, string folderPath)
{
    /// <summary>
    /// Gets the folder's full path, for plain files beside the provider's; a provider that keeps entries, not files, has
    /// nothing there.
    /// </summary>
    public string FolderPath { get; } = Path.GetFullPath(folderPath);

    /// <summary>Opens a folder inside this one.</summary>
    /// <param name="name">The folder's name.</param>
    /// <returns>The folder's storage.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is a path, empty, <c>.</c> or <c>..</c>.</exception>
    public Storage OpenStorage(string name) => new(provider, GetPath(name));

    /// <summary>Reads a file's text.</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such file.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is a path, empty, <c>.</c> or <c>..</c>.</exception>
    public string? Read(string name) => provider.Read(GetPath(name));

    /// <summary>Writes a file's text, whole or not at all.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="content">The text.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is a path, empty, <c>.</c> or <c>..</c>.</exception>
    public void Write(string name, string content) => provider.Write(GetPath(name), content);

    /// <summary>Deletes a file; a missing one is passed over.</summary>
    /// <param name="name">The file's name.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is a path, empty, <c>.</c> or <c>..</c>.</exception>
    public void Delete(string name) => provider.Delete(GetPath(name));

    /// <summary>Gets a signature of a file, which changes when the file is written as a rule.</summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The signature.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is a path, empty, <c>.</c> or <c>..</c>.</exception>
    public string GetSignature(string name) => provider.GetSignature(GetPath(name));

    // ':' too: on Windows "C:" is rooted, and Path.Combine would drop the folder for it
    private string GetPath(string name)
    {
        if (name is "" or "." or ".." || name.AsSpan().IndexOfAny('/', '\\', ':') >= 0)
            throw new ArgumentException($"A storage takes a name, not a path. Name: {name}", nameof(name));

        return Path.Combine(FolderPath, name);
    }
}
