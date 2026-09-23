namespace VpnHood.Net.Toolkit.Assets;

// A file, as an asset: for a caller that was given a path rather than a provider - a test, a tool,
// a head that already knows where the thing lies. The same contract as every other asset, so what
// takes it cannot tell: a new stream every call, owned by the caller, and no file at that path is
// AssetNotFoundException rather than the plain one, which TryOpenReadAsync turns into null.
public class FileAsset(string filePath) : IAsset
{
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new AssetNotFoundException(filePath);

        return Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public override string ToString()
    {
        return filePath;
    }
}
