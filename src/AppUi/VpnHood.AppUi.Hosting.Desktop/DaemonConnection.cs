using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppLib.App.WebHosting;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Channel;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop;

// The way in to the app's API, followed as it moves: the service's, over its channel
// (DaemonChannelConnector), or this process's own (CreateInProcess).
public sealed class DaemonConnection : IAsyncDisposable
{
    private readonly IDaemonAnswerProvider _answerProvider;
    private readonly DaemonApiUrlProvider _apiUrlProvider;
    private readonly HttpClient _httpClient;

    internal DaemonConnection(IDaemonAnswerProvider answerProvider, ILoopbackPeerCheck peerCheck, string instanceName,
        string notRunningHint)
    {
        _answerProvider = answerProvider;
        _apiUrlProvider = new DaemonApiUrlProvider(answerProvider);

        // The base address is a stand-in: the handler puts the latest one on every request.
        _httpClient = new HttpClient(new DaemonApiHandler(answerProvider, peerCheck, instanceName, notRunningHint)) {
            BaseAddress = new Uri("http://127.0.0.1/"),
            Timeout = TimeSpan.FromMinutes(2) // a connect attempt walks a server list and may be slow
        };

        Api = VpnHoodApiHttpFactory.Create(_httpClient);
        _answerProvider.Changed += AnswerProvider_Changed;
    }

    public VpnHoodApi Api { get; }

    // A service that answers as a newer version than this binary: an update replaced it under an open window, which
    // starts itself again on the new version (UiCommand).
    public event EventHandler? NewerServiceAnswered;

    private void AnswerProvider_Changed(object? sender, EventArgs e)
    {
        if (Version.TryParse(_answerProvider.Current?.Version, out var serviceVersion) &&
            Version.TryParse(DaemonChannelAnswer.CurrentVersion, out var version) && serviceVersion > version)
            NewerServiceAnswered?.Invoke(this, EventArgs.Empty);
    }

    // The daemon's API URL, followed as it moves: what a web view loads, token and all.
    public IDesktopApiUrlProvider ApiUrlProvider => _apiUrlProvider;

    // The app in this very process (InProcessDesktopHost), over its own local web host, which must be
    // started. It answers while this process runs, so it is never found stopped.
    internal static DaemonConnection CreateInProcess(IAppWebHost webHost, ILoopbackPeerCheck peerCheck,
        string instanceName)
    {
        return new DaemonConnection(new InProcessAnswerProvider(webHost), peerCheck, instanceName,
            notRunningHint: $"{instanceName} has stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        _answerProvider.Changed -= AnswerProvider_Changed;
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
