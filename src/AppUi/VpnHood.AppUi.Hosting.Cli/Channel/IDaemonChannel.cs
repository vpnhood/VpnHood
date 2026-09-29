namespace VpnHood.AppUi.Hosting.Cli.Channel;

// Where the service hands its API's address and token, to an administrator alone: a named pipe on
// Windows, a Unix socket on Linux, each telling who calls.
public interface IDaemonChannel
{
    // The service's side: takes callers until disposed.
    Task<IDaemonChannelListener> Listen(CancellationToken cancellationToken);

    // A client's side: throws when nobody serves the channel, or someone not trusted does.
    Task<Stream> Connect(CancellationToken cancellationToken);
}
