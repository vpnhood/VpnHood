namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Represents a file as an asset, for a caller that was given a path rather than a provider: a test, a tool, or an app
/// that already knows where the file lies.
/// </summary>
/// <remarks>
/// It keeps every asset's contract, so what takes it cannot tell: a missing file throws
/// <see cref="AssetNotFoundException"/>, not a plain <see cref="FileNotFoundException"/>. Absence is what
/// <see cref="File.Exists"/> tells, so a file it cannot see, such as one without read permission, counts as absent.
/// </remarks>
/// <param name="filePath">The file's path.</param>
public class FileAsset(string filePath) : IAsset
{
    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        // the open itself tells that the file is missing: a look before it could find a file that is gone a moment
        // later
        try {
            return Task.FromResult<Stream>(File.OpenRead(filePath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            throw new AssetNotFoundException(filePath, ex);
        }
    }

    /// <summary>Returns the file's path.</summary>
    /// <returns>The path.</returns>
    public override string ToString()
    {
        return filePath;
    }
}
