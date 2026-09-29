namespace VpnHood.AppUi.Hosting.Cli.Channel;

// The daemon's latest answer: its API URL, token and process. Null while nothing answers, so a call
// then fails at once rather than go to an old URL.
internal interface IDaemonAnswerProvider : IAsyncDisposable
{
    DaemonChannelAnswer? Current { get; }

    // The service's no, once it said it; a connection then fails every call with it.
    string? Refusal { get; }

    // Current or Refusal changed. From any thread.
    event EventHandler? Changed;
}
