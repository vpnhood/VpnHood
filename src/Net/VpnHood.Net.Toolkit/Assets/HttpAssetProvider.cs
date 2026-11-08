using System.Net;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Assets;

/// <summary>
/// Provides the assets a web server serves, each at the base URL followed by the asset path, for a caller with nothing
/// placed beside it.
/// </summary>
/// <remarks>
/// Status 404 is an absent asset; any other failure is its own. The body is read as it arrives, forward and not
/// buffered; disposing the stream gives the connection back.
/// </remarks>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="baseUrl">The URL the asset paths are appended to.</param>
public class HttpAssetProvider(HttpClient httpClient, string baseUrl) : IAssetProvider
{
    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed, or its status is a failure other than 404.</exception>
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
