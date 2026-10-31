namespace VpnHood.Net.Http3;

public interface IHttp3StreamFilter
{
    // The prefix is borrowed for this call. NeedMore cannot extend the engine's inspection bound.
    Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken);
}
