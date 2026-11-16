namespace VpnHood.Net.Http3;

/// <summary>
/// Represents what runs the streams a filter claimed.
/// </summary>
public interface IHttp3StreamRunner
{
    /// <summary>Runs a stream the filter claimed.</summary>
    /// <param name="context">The endpoints of the stream's connection.</param>
    /// <param name="stream">The stream, which reads its first bytes again.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests; the connection's close cancels it.</param>
    /// <returns>
    /// A task that covers the stream's whole use, also when another component takes it over: the server disposes the
    /// stream when it completes.
    /// </returns>
    Task Run(Http3StreamContext context, Stream stream, CancellationToken cancellationToken);
}
