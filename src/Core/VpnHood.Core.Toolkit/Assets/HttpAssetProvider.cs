using System.Net;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Toolkit.Assets;

// Assets fetched from a web server by name, at "<baseUrl><assetPath>": for a caller with nothing
// placed beside it, which reads over the network instead. 404 is the absent asset; any other
// failure is its own.
//
// The body as it arrives, read forward and not buffered - what to do about that is the caller's
// decision, not this one's.
public class HttpAssetProvider(HttpClient httpClient, string baseUrl) : IAssetProvider
{
    public async Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
    {
        var response = await httpClient
            .GetAsync(baseUrl + assetPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).Vhc();
        try {
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new AssetNotFoundException(assetPath);

            response.EnsureSuccessStatusCode();

            // The stream is what holds the connection, and disposing it gives the connection back -
            // which the caller does, as it does for every other provider. What is left of the
            // response is headers and no unmanaged thing, so it is not disposed here and nothing
            // has to outlive the read to do it later.
            return await response.Content.ReadAsStreamAsync(cancellationToken).Vhc();
        }
        catch {
            response.Dispose();
            throw;
        }
    }
}
