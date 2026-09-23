namespace VpnHood.Core.Toolkit.Assets;

// One thing to open: a provider with the path already chosen, and nothing else. What travels when a
// component is handed something to read - a value that says WHICH asset it is, so a failure can
// name it, where a bare delegate could say only that something failed.
//
// An interface rather than only Asset, because where the bytes come from is the caller's business -
// a file a build placed, an entry of an archive, an array a test made. Take this in a signature;
// construct Asset.
//
// The same contract as the provider it wraps, with nothing added: a NEW stream every call, owned
// and disposed by the caller, read forward, and AssetNotFoundException when there is none. A caller
// that is only asking takes TryOpenReadAsync.
public interface IAsset
{
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}
