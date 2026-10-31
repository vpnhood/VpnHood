namespace VpnHood.Net.Http3;

public interface IHttp3StreamRunner
{
    // The task covers the stream's whole life, including ownership handed to another component.
    Task Run(Http3StreamContext context, Stream stream, CancellationToken cancellationToken);
}
