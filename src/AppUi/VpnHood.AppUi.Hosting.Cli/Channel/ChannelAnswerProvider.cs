using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// The daemon's answer as its channel sends it, over one connection kept open. When that goes, so
// does the answer, and it asks again until the service answers or says no.
internal sealed class ChannelAnswerProvider : IDaemonAnswerProvider
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);

    private readonly IDaemonChannel _channel;
    private readonly string _instanceName;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _following;
    private DaemonChannelAnswer? _current;
    private string? _refusal;
    private bool _outageLogged;

    public ChannelAnswerProvider(IDaemonChannel channel, string instanceName)
    {
        _channel = channel;
        _instanceName = instanceName;
        _following = Follow(_cancellation.Token);
    }

    public DaemonChannelAnswer? Current {
        get {
            lock (_lock)
                return _current;
        }
    }

    public string? Refusal {
        get {
            lock (_lock)
                return _refusal;
        }
    }

    public event EventHandler? Changed;

    private async Task Follow(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await using var stream = await _channel.Connect(cancellationToken).Vhc();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var answer = await DaemonChannelClient.Ask(stream, reader, cancellationToken).Vhc() ??
                             throw new IOException("The channel closed before answering.");

                // A no is final for this process: whoever runs it may not use the app.
                if (answer.Refusal != null) {
                    Set(null, answer.Refusal);
                    return;
                }

                Set(answer, null);
                _outageLogged = false;
                while (await DaemonChannelLines.Read<DaemonChannelAnswer>(reader, cancellationToken).Vhc() is { } next)
                    Set(next, null);

                VhLogger.Instance.LogDebug("{InstanceName} closed its channel; asking again.", _instanceName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) {
                // once per outage: a stopped service would otherwise fill the log four times a second
                if (!_outageLogged) {
                    VhLogger.Instance.LogDebug(ex, "{InstanceName}'s channel is not answering; asking again.", _instanceName);
                    _outageLogged = true;
                }
            }

            Set(null, null);
            try {
                await Task.Delay(RetryInterval, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) {
                return;
            }
        }
    }

    private void Set(DaemonChannelAnswer? answer, string? refusal)
    {
        bool changed;
        lock (_lock) {
            changed = !ReferenceEquals(_current, answer) || _refusal != refusal;
            _current = answer;
            _refusal = refusal;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().Vhc();
        await _following.Vhc();
        _cancellation.Dispose();
    }
}
