using VpnHood.Net.Http3;

namespace VpnHood.Test.Providers;

internal sealed class Http3EchoTunnelApplication : IHttp3StreamFilter, IHttp3StreamRunner, IHttp3Responder
{
    private readonly TaskCompletionSource _endOfStreamObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task EndOfStreamObserved => _endOfStreamObserved.Task;

    public Task<Http3StreamDecision> Filter(Http3StreamContext context, ReadOnlyMemory<byte> prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(prefix.IsEmpty ? Http3StreamDecision.NeedMore :
            prefix.Span[0] == 0x42 ? Http3StreamDecision.Claimed : Http3StreamDecision.NotClaimed);
    }

    public async Task Run(Http3StreamContext context, Stream stream, CancellationToken cancellationToken)
    {
        var marker = new byte[1];
        await stream.ReadExactlyAsync(marker, cancellationToken);
        Assert.AreEqual((byte)0x42, marker[0], "The runner must receive the prefix consumed by admission.");
        var message = new byte[2];
        try {
            while (true) {
                await stream.ReadExactlyAsync(message, cancellationToken);
                await stream.WriteAsync(message, cancellationToken);
            }
        }
        catch (EndOfStreamException) {
            _endOfStreamObserved.TrySetResult();
            throw;
        }
    }

    public Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The echo tunnel must be claimed before HTTP/3 request parsing.");
}
