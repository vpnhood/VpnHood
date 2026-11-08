namespace VpnHood.Net.Http3;

/// <summary>
/// Represents what answers the HTTP/3 requests no filter claimed.
/// </summary>
public interface IHttp3Responder
{
    /// <summary>Writes the response to a request.</summary>
    /// <remarks>
    /// The request's body is not handed over: while the responder runs, and after, the server inspects up to
    /// <see cref="Http3ServerOptions.MaxBodyInspectionBytes"/> after the headers to check it, and discards it.
    /// </remarks>
    /// <param name="request">The request.</param>
    /// <param name="response">The response, to write with <see cref="Http3ResponseWriter.WriteHeaders"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation; the server sends the response when it completes.</returns>
    Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken);
}
