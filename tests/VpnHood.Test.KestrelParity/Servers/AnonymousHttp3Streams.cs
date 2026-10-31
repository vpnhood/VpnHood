using VpnHood.Net.Http3;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class AnonymousHttp3Streams : IHttp3StreamFilter, IHttp3StreamRunner
{
    public Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Http3StreamDecision.NotClaimed);
    }

    public Task Run(Http3StreamContext context, Stream stream, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A controlled anonymous request cannot claim a tunnel.");
}
