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

    /// <summary>Initializes a new instance of the <see cref="FolderAssetProvider"/> class.</summary>
    /// <param name="folderPath">The folder; a relative path is taken from the current directory.</param>
    public FolderAssetProvider(string folderPath)
    {
        _folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        // Under the folder or nothing: an asset path may come from outside, and ".." is not an
        // asset. The folder itself is not a file either.
        var path = Path.GetFullPath(Path.Combine(_folderPath, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        var isInside = path.StartsWith(_folderPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!isInside || !File.Exists(path))
            throw new AssetNotFoundException(assetPath);

        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}
