namespace VpnHood.Net.Http3;

/// <summary>
/// Specifies what <see cref="IHttp3StreamFilter"/> decides for a request stream.
/// </summary>
public enum Http3StreamDecision
{
    /// <summary>The stream is an HTTP/3 request: the server processes it.</summary>
    NotClaimed,

    /// <summary>The stream is the filter's: the server hands it to <see cref="IHttp3StreamRunner"/>.</summary>
    Claimed,

    /// <summary>
    /// The filter needs more of the stream's start to decide. The server reads more, within
    /// <see cref="Http3ServerOptions.MaxFilterBytes"/> and the HTTP/3 frame the bytes begin, and treats the stream as
    /// not claimed when it can read no more.
    /// </summary>
    NeedMore
}
