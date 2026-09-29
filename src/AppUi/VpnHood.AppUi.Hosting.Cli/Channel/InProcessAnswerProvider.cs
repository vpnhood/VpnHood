using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppUi.Hosting.Cli.Channel;

// The answer for the app in this very process (the "dev" command): its local web host's URL, read
// again whenever that host rebinds. Nobody refuses, and the process to reach is this one.
internal sealed class InProcessAnswerProvider : IDaemonAnswerProvider
{
    private readonly IAppWebHost _webHost;

    public InProcessAnswerProvider(IAppWebHost webHost)
    {
        _webHost = webHost;
        Current = Read();
        _webHost.Restarted += OnRestarted;
    }

    public DaemonChannelAnswer? Current { get; private set; }
    public string? Refusal => null;
    public event EventHandler? Changed;

    private DaemonChannelAnswer Read()
    {
        return new DaemonChannelAnswer {
            ApiUrl = _webHost.Urls.Count > 0
                ? _webHost.Urls[0]
                : throw new InvalidOperationException("The local web host has not been started."),
            ProcessId = Environment.ProcessId,
            Version = DaemonChannelAnswer.CurrentVersion
        };
    }

    private void OnRestarted(object? sender, EventArgs e)
    {
        Current = Read();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        _webHost.Restarted -= OnRestarted;
        return ValueTask.CompletedTask;
    }
}
