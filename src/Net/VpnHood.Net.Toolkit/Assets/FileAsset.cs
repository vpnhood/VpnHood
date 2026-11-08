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
        if (!File.Exists(filePath))
            throw new AssetNotFoundException(filePath);

        return Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    /// <summary>Returns the file's path.</summary>
    /// <returns>The path.</returns>
    public override string ToString()
    {
        return filePath;
    }
}
