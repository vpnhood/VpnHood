namespace VpnHood.AppUi.Hosting.Cli.Abstractions;

// One caller on the channel: its stream, and who it is, which the platform reads off the connection
// itself (a pipe names the calling process; a Unix socket gives its user and groups).
public interface IDaemonChannelCaller : IAsyncDisposable
{
    Stream Stream { get; }

    // Asked only after the caller's first line has been read: Windows lets the service impersonate
    // its caller, which is how it reads the caller's token, only once something has come over the pipe.
    DaemonCallerInfo Identify();
}
