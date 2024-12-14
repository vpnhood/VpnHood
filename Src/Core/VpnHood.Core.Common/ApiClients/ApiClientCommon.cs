using System.Net.Http.Headers;
using System.Text;

namespace VpnHood.Core.Common.ApiClients;

public abstract class ApiClientCommon
{
    public Uri? DefaultBaseAddress { get; set; }
    public AuthenticationHeaderValue? DefaultAuthorization { get; set; }
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder,
        CancellationToken ct)
    {
        return PrepareRequestAsync(client, request, urlBuilder.ToString(), ct);
    }

    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, string url,
        CancellationToken ct)
    {
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

    protected virtual Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}