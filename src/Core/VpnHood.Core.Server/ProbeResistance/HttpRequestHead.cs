namespace VpnHood.Core.Server.ProbeResistance;

// a request's line and headers as Kestrel reads them, and how its body comes
internal sealed class HttpRequestHead
{
    public required string Method { get; init; }
    public required string Target { get; init; } // as sent
    public required bool IsHttp10 { get; init; }
    public required bool IsHostMissing { get; init; }

    // a repeated header's values joined with commas, as ASP.NET Core joins them
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required int HeaderCount { get; init; }
    public required int HeaderSize { get; init; }
    public required long? ContentLength { get; init; }
    public required bool IsChunked { get; init; }

    // whether the connection may carry another request after this one's reply: what the request asks, unless it
    // is a CONNECT or carries both a length and chunks, which Kestrel answers and closes
    public required bool KeepAlive { get; init; }

    public bool IsHead => Method == "HEAD";
    public string RequestLine => $"{Method} {Target} HTTP/1.{(IsHttp10 ? 0 : 1)}";
}
