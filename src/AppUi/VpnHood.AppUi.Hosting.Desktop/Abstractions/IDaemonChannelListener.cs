namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// The channel as the service holds it open: the next caller, each time one connects.
public interface IDaemonChannelListener : IAsyncDisposable
{
    Task<IDaemonChannelCaller> Accept(CancellationToken cancellationToken);
}
