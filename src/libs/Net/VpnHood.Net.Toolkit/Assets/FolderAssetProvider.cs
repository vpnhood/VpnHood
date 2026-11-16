namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides the assets under a folder, read where they lie: the folder beside an executable, an app bundle, or a
/// folder something else unpacked.
/// </summary>
/// <remarks>
/// A path that leads outside the folder, such as one with <c>..</c>, counts as an absent asset; a link on the disk may
/// still lead out. Absence is what <see cref="File.Exists"/> tells.
/// </remarks>
public class FolderAssetProvider : IAssetProvider
{
    private readonly string _folderPath;
    private readonly string _folderPrefix; // the folder's path with one separator after it

    /// <summary>Initializes a new instance of the <see cref="FolderAssetProvider"/> class.</summary>
    /// <param name="folderPath">The folder; a relative path is taken from the current directory.</param>
    public FolderAssetProvider(string folderPath)
    {
        _folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));

        // a root keeps its separator, which a second one would make a prefix no file has
        _folderPrefix = Path.EndsInDirectorySeparator(_folderPath)
            ? _folderPath
            : _folderPath + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        // Under the folder or nothing: an asset path may come from outside, and ".." is not an
        // asset. The folder itself is not a file either.
        var path = Path.GetFullPath(Path.Combine(_folderPath, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        var isInside = path.StartsWith(_folderPrefix, StringComparison.Ordinal);
        if (!isInside || !File.Exists(path))
            throw new AssetNotFoundException(assetPath);

        // the file may go between the look above, which also tells a folder from a file, and the open
        try {
            return Task.FromResult<Stream>(File.OpenRead(path));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            throw new AssetNotFoundException(assetPath, ex);
        }
    }
}
