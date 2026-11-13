namespace VpnHood.Net.Http3;

/// <summary>
/// Provides the limits and timeouts of an <see cref="Http3ServerConnection"/>. Its constructor checks them and throws
/// <see cref="ArgumentOutOfRangeException"/> for a negative limit, or a zero one where noted, and for a timeout that
/// is not above zero, is longer than a timer takes (about 49 days), and is not
/// <see cref="Timeout.InfiniteTimeSpan"/>.
/// </summary>
public sealed class Http3ServerOptions
{
    // the longest a CancellationTokenSource or a task's wait times
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Gets the most streams the connection processes at once; accepting waits while it has that many. It must be more
    /// than zero. The default is 110.
    /// </summary>
    public int MaxConcurrentStreams { get; init; } = 110;

    /// <summary>
    /// Gets the most bytes of a request stream's start the filter sees before the stream is treated as an HTTP/3
    /// request. It must be more than zero. The default is 4096.
    /// </summary>
    public int MaxFilterBytes { get; init; } = 4096;

    /// <summary>
    /// Gets the most bytes of header fields a request may send, headers and trailers together, counting each field's
    /// name and value; the connection advertises it to the peer. The server checks it once, when it chooses the
    /// response: a request over it then gets status 431, and trailers read later do not change the response. Fields over
    /// twice it get the stream rejected. The default is 32768.
    /// </summary>
    public int MaxHeaderBytes { get; init; } = 32768;

    /// <summary>
    /// Gets the most header fields a request may send, headers and trailers together. The server checks it once, when it
    /// chooses the response: a request over it then gets status 431. Fields over twice it get the stream rejected. The
    /// default is 100.
    /// </summary>
    public int MaxHeaderCount { get; init; } = 100;

    /// <summary>
    /// Gets the most bytes of one literal QPACK string, a field's name or value, without its length prefix: as received,
    /// and for a Huffman-coded string also as decoded. Indexed fields are not counted. The default is 32768.
    /// </summary>
    public int MaxFieldBytes { get; init; } = 32768;

    /// <summary>
    /// Gets the most characters of a request's decoded method, scheme, authority and path together; a longer request is
    /// rejected. A CONNECT request without <c>:protocol</c> and <c>OPTIONS *</c> are not checked. The default is 8192.
    /// </summary>
    public int MaxRequestLineBytes { get; init; } = 8192;

    /// <summary>
    /// Gets the most bytes the server inspects after a request's headers, frames and trailers included, to check the body
    /// against its content length; the body is discarded. The inspection runs while the responder does and may go on
    /// after it, and a read may buffer more than it inspects. A longer body is not checked to its end. The default is
    /// 64 KB.
    /// </summary>
    public long MaxBodyInspectionBytes { get; init; } = 64 * 1024;

    /// <summary>
    /// Gets the size from which the payload of a SETTINGS frame, or of an unknown frame, on the peer's control stream is
    /// too large: it must be shorter. It must be more than zero. The default is 10000.
    /// </summary>
    public int MaxControlFrameBytes { get; init; } = 10_000;

    /// <summary>
    /// Gets how long a request may take, from its stream's start through its headers, the body's inspection and the
    /// response, and how long a unidirectional stream of the peer may take to tell its type. A claimed stream is not
    /// timed. The default is 30 seconds.
    /// </summary>
    public TimeSpan HeadersTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long the connection stays open with no request in progress before it closes; it stops applying once a
    /// graceful close begins. The default is 130 seconds.
    /// </summary>
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(130);

    /// <summary>
    /// Gets how long each disposal of a close, such as a stream's or the connection's, may take. The default is
    /// 5 seconds.
    /// </summary>
    /// <remarks>
    /// <see cref="Http3ServerConnection.Run"/> also waits for the streams' runners and responders to end, with no time
    /// limit.
    /// </remarks>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the function that gives the header fields of every response from its status code, the server's own 431
    /// included. It must not be <see langword="null"/>; the default gives none.
    /// </summary>
    public Func<int, IReadOnlyList<KeyValuePair<string, string>>> ResponseHeaders { get; init; } = static _ => [];

    internal Http3ServerOptions Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConcurrentStreams);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFilterBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxHeaderBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxHeaderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxFieldBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRequestLineBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBodyInspectionBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxControlFrameBytes);
        ArgumentNullException.ThrowIfNull(ResponseHeaders);

        // here, not at their first use: Run arms the idle timer before its cleanup is in place, so a timeout a
        // timer refuses would end it with the connection left open
        ThrowIfNoTimeout(HeadersTimeout, nameof(HeadersTimeout));
        ThrowIfNoTimeout(KeepAliveTimeout, nameof(KeepAliveTimeout));
        ThrowIfNoTimeout(CloseTimeout, nameof(CloseTimeout));
        return this;
    }

    private static void ThrowIfNoTimeout(TimeSpan timeout, string name)
    {
        if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout > MaxTimeout))
            throw new ArgumentOutOfRangeException(name, timeout,
                $"{name} must be more than zero and at most {MaxTimeout}, or Timeout.InfiniteTimeSpan.");
    }
}
