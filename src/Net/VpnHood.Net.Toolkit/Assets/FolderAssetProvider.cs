namespace VpnHood.Net.Toolkit.Assets;

// Assets under a folder, read where they lie: the directory beside an executable, an app bundle, a
// folder something else unpacked. Which folder is the caller's to say, so nothing here assumes what
// the files are for.
public class FolderAssetProvider : IAssetProvider
{
    private readonly string _folderPath;

    public FolderAssetProvider(string folderPath)
    {
        _folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

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
