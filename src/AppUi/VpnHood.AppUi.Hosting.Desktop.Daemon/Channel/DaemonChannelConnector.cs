using VpnHood.AppUi.Hosting.Desktop.Channel;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.AppUi.Hosting.Desktop.Exceptions;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Channel;

// The daemon's API over the service's channel. Open waits for a service still coming up - the first
// command after an install meets one - but not for one stopped.
public static class DaemonChannelConnector
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // A standard user is refused before the channel is asked: told at once, rather than after a wait
    // for a service that would only refuse them, or a hint to start one they may not start.
    public static Task<DaemonConnection> Open(IDesktopPlatform platform, CancellationToken cancellationToken)
    {
        if (!platform.IsAdministrator())
            throw new DaemonRefusedException(platform.AdministratorsOnlyMessage);

        return Open(platform.Channel, platform.PeerCheck, platform.Instance, platform.Paths, cancellationToken);
    }

    internal static async Task<DaemonConnection> Open(IDaemonChannel channel, ILoopbackPeerCheck peerCheck,
        IAppInstanceController instance, IAppDesktopPaths paths, CancellationToken cancellationToken)
    {
        var answerProvider = new ChannelAnswerProvider(channel, paths.InstanceName);
        try {
            await WaitForAnswer(answerProvider, instance, paths, cancellationToken).Vhc();
            return new DaemonConnection(answerProvider, peerCheck, paths.InstanceName, instance.NotRunningHint);
        }
        catch {
            await answerProvider.DisposeAsync().Vhc();
            throw;
        }
    }

    private static async Task WaitForAnswer(IDaemonAnswerProvider answerProvider, IAppInstanceController instance,
        IAppDesktopPaths paths, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BindTimeout);

        try {
            while (true) {
                if (answerProvider.Refusal is { } refusal)
                    throw new DaemonRefusedException(refusal);

                if (answerProvider.Current != null)
                    return;

                // Nothing is starting, so there is nothing to wait for; and one that died while we
                // waited is said so rather than spend the whole timeout on it.
                if (!await instance.IsRunning(cancellationToken).Vhc())
                    throw new DaemonNotRunningException(instance.NotRunningHint);

                await WaitForChange(answerProvider, PollInterval, timeout.Token).Vhc();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new InvalidOperationException(
                $"{paths.InstanceName} is running but has not answered. " +
                $"See: {paths.CommandName} service log");
        }
    }

    // Until the answer changes, or the interval passes: the answer is taken the moment it comes.
    private static async Task WaitForChange(IDaemonAnswerProvider answerProvider, TimeSpan interval,
        CancellationToken cancellationToken)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e) => changed.TrySetResult();

        answerProvider.Changed += OnChanged;
        try {
            await Task.WhenAny(changed.Task, Task.Delay(interval, cancellationToken)).Vhc();
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally {
            answerProvider.Changed -= OnChanged;
        }
    }
}
