namespace VpnHood.Net.Http3;

/// <summary>
/// Represents what decides, from a request stream's first bytes, whether the stream is an HTTP/3 request or belongs to
/// another protocol sharing the connection.
/// </summary>
public interface IHttp3StreamFilter
{
    /// <summary>Decides whether a request stream is the filter's.</summary>
    /// <param name="context">The endpoints of the stream's connection.</param>
    /// <param name="prefix">The stream's first bytes, lent for this call only.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests; <see cref="Http3ServerOptions.HeadersTimeout"/> cancels it too.
    /// </param>
    /// <returns>A task whose result is the decision.</returns>
    Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken);
}
