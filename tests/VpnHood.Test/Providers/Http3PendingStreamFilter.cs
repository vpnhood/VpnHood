using VpnHood.Net.Http3;

namespace VpnHood.Test.Providers;

internal sealed class Http3PendingStreamFilter : IHttp3StreamFilter
{
    public int LastPrefixLength { get; private set; }

    public Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastPrefixLength = prefix.Length;
        return Task.FromResult(Http3StreamDecision.NeedMore);
    }
}
