using System.Net.Http.Headers;
using System.Text;

// ReSharper disable CollectionNeverUpdated.Global

namespace VpnHood.Net.Toolkit.ApiClients;

/// <summary>
/// Provides the base of a generated API client: the request's base address, authorization and headers, through the
/// hooks the generated code calls.
/// </summary>
public abstract class ApiClientCommon
{
    /// <summary>Gets or sets the address a relative request's URL is taken from, or <see langword="null"/>.</summary>
    public Uri? DefaultBaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the authorization a request without its own carries, or <see langword="null"/> for none.
    /// </summary>
    public AuthenticationHeaderValue? DefaultAuthorization { get; set; }

    /// <summary>Gets or sets the headers every request carries. The default is none.</summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>Prepares a request before it is sent, as the URL-string overload does.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="urlBuilder">The request's URL.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder,
        CancellationToken cancellationToken)
    {
        return PrepareRequestAsync(client, request, urlBuilder.ToString(), cancellationToken);
    }

    /// <summary>
    /// Prepares a request before it is sent: sets its URL, on <see cref="DefaultBaseAddress"/> when relative, its
    /// <see cref="DefaultAuthorization"/> unless it has one, and the <see cref="DefaultHeaders"/>.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="url">The request's URL, absolute or relative.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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

    /// <summary>Processes a response before it is read; the base does nothing.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected virtual Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        _ = client;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}