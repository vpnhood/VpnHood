using System.Net.Http.Headers;
using System.Text;
// ReSharper disable CollectionNeverUpdated.Global

namespace VpnHood.Core.Toolkit.ApiClients;

public abstract class ApiClientCommon
{
    public Uri? DefaultBaseAddress { get; set; }
    public AuthenticationHeaderValue? DefaultAuthorization { get; set; }
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder,
        CancellationToken cancellationToken)
    {
        return PrepareRequestAsync(client, request, urlBuilder.ToString(), cancellationToken);
    }

    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, string url, 
        CancellationToken cancellationToken)
    {
        _ = url;
        _ = client;
        _ = cancellationToken;

        // build url
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);

        // add DefaultBaseAddress if exists and request uri is relative
        if (DefaultBaseAddress != null && !request.RequestUri.IsAbsoluteUri)
            request.RequestUri = new Uri(DefaultBaseAddress, request.RequestUri);

        // add default authorization header
        request.Headers.Authorization ??= DefaultAuthorization;

        // add default headers
        foreach (var header in DefaultHeaders)
            request.Headers.Add(header.Key, header.Value);

        return Task.CompletedTask;
    }

    protected virtual Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, 
        CancellationToken cancellationToken)
    {
        _ = client;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}