using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppLib.App.WebHosting;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Channel;
using VpnHood.AppUi.Hosting.Desktop.Exceptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop;

// The way in to the daemon's API, followed over the service's channel as it moves. Open waits for a
// service still coming up - the first command after an install meets one - but not for one stopped.
public sealed class DaemonConnection : IAsyncDisposable
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IDaemonAnswerProvider _answerProvider;
    private readonly DaemonApiUrlProvider _apiUrlProvider;
    private readonly HttpClient _httpClient;

    private DaemonConnection(IDaemonAnswerProvider answerProvider, ILoopbackPeerCheck peerCheck,
        IAppInstanceController instance, string instanceName)
    {
        _answerProvider = answerProvider;
        _apiUrlProvider = new DaemonApiUrlProvider(answerProvider);

        // The base address is a stand-in: the handler puts the latest one on every request.
        _httpClient = new HttpClient(new DaemonApiHandler(answerProvider, peerCheck, instanceName, instance.NotRunningHint)) {
            BaseAddress = new Uri("http://127.0.0.1/"),
            Timeout = TimeSpan.FromMinutes(2) // a connect attempt walks a server list and may be slow
        };

        Api = VpnHoodApiHttpFactory.Create(_httpClient);
    }

    public VpnHoodApi Api { get; }

    // The daemon's API URL, followed as it moves: what a web view loads, token and all.
    public IDesktopApiUrlProvider ApiUrlProvider => _apiUrlProvider;

    // A standard user is refused before the channel is asked: told at once, rather than after a wait
    // for a service that would only refuse them, or a hint to start one they may not start.
    public static Task<DaemonConnection> Open(DesktopPlatform platform, CancellationToken cancellationToken)
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
            return new DaemonConnection(answerProvider, peerCheck, instance, paths.InstanceName);
        }
        catch {
            await answerProvider.DisposeAsync().Vhc();
            throw;
        }
    }

    // The app in this very process (DevCommand), over its own local web host, which must be started.
    internal static DaemonConnection CreateInProcess(IAppWebHost webHost, ILoopbackPeerCheck peerCheck,
        IAppInstanceController instance, string instanceName)
    {
        return new DaemonConnection(new InProcessAnswerProvider(webHost), peerCheck, instance, instanceName);
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

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        _apiUrlProvider.Dispose();
        await _answerProvider.DisposeAsync().Vhc();
    }

    // The window's API URL. While nothing answers it is the last one known, which a load fails
    // against and is tried again on; a new URL is a change, an outage is not.
    private sealed class DaemonApiUrlProvider : IDesktopApiUrlProvider, IDisposable
    {
        private readonly IDaemonAnswerProvider _answerProvider;
        private Uri _last;

        public DaemonApiUrlProvider(IDaemonAnswerProvider answerProvider)
        {
            _answerProvider = answerProvider;
            _last = answerProvider.Current?.ApiUrl ?? throw new InvalidOperationException("The service has not answered.");
            _answerProvider.Changed += OnChanged;
        }

        public Uri Current {
            get {
                if (_answerProvider.Current?.ApiUrl is { } url)
                    _last = url;
                return _last;
            }
        }

        public event EventHandler? Changed;

        private void OnChanged(object? sender, EventArgs e)
        {
            if (_answerProvider.Current?.ApiUrl != null)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _answerProvider.Changed -= OnChanged;
        }
    }
}
