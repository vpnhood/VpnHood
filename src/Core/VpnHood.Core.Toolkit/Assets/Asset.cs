namespace VpnHood.Core.Toolkit.Assets;

// One asset an asset package's build placed: where it is, and the platform that knows how to read
// it. Not necessarily a file - on Android it is a byte range inside the package, and a provider is
// free to serve one from anywhere - which is why nothing outside can ask where it lives.
//
// Handed about as a value: a head builds one and the engine keeps it, so what travels says WHICH
// asset it is, and a failure can name it, where a bare delegate could say only that something
// failed. ToString is that name, and the only reason anything outside needs it.
//
// Opening is what this does, and every call opens a NEW stream. That is not a rule to remember:
// there is no stream here to share by mistake, which is exactly why this exists rather than a
// Func<Stream>. The caller owns and disposes what it gets.
public class Asset(IAssetProvider assetProvider, string assetPath)
{
    public Stream OpenRead()
    {
        return assetProvider.OpenRead(assetPath);
    }

    public override string ToString()
    {
        return assetPath;
    }
}
