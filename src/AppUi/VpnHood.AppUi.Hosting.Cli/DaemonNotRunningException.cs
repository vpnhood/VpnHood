namespace VpnHood.AppUi.Hosting.Cli;

// Nothing to talk to: no daemon has published an address and the platform says none is running.
// The message is the platform's own sentence (IAppInstanceController.NotRunningHint), because
// how to start it is the one thing the reader wants and the one thing this project cannot know.
public class DaemonNotRunningException(string notRunningHint) : InvalidOperationException(notRunningHint);
