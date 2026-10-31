namespace VpnHood.Net.Http3;

public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; init; } = 110;
    public int MaxFilterBytes { get; init; } = 4096;
    public int MaxHeaderBytes { get; init; } = 32768;
    public int MaxHeaderCount { get; init; } = 100;
    public int MaxFieldBytes { get; init; } = 32768;
    public int MaxRequestLineBytes { get; init; } = 8192;
    public long MaxBodyInspectionBytes { get; init; } = 64 * 1024;
    public int MaxControlFrameBytes { get; init; } = 10_000;
    public TimeSpan HeadersTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(130);
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // Called for every response, including the engine's header-limit response.
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
        return this;
    }
}
