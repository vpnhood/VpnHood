namespace VpnHood.AppUi.Hosting.Cli.Channel;

// Who is at the other end of a channel connection, and whether they may use the app. Name is for
// the service's log: an account name on Windows, a user name on Linux.
public record DaemonCallerInfo(string Name, bool IsAdministrator);
