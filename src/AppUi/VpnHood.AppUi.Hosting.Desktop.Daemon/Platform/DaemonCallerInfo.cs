namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

/// <summary>Represents who is at the other end of a channel connection, and whether they may use the app.</summary>
/// <param name="Name">
/// The caller's name, for the service's log: an account name on Windows, a user name on Linux, or <c>uid N</c> where the
/// user has none.
/// </param>
/// <param name="IsAdministrator"><see langword="true"/> where the caller is an administrator, who may use the app.</param>
public record DaemonCallerInfo(string Name, bool IsAdministrator);
