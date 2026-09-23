namespace VpnHood.AppUi.Hosting.Cli;

// What the platform says for itself: the two answers every command reaches for that are not the
// app's, and the one that is optional. A platform package (Cli.Linux, Cli.Win) builds one of these
// and hands it to CliHost with the head's CliHeadParams; nothing in this project reads a static.
public class CliPlatform
{
    public required IAppCliPaths Paths { get; init; }
    public required IAppInstanceController Instance { get; init; }

    // How to BE the daemon, for a platform whose instance is a headless process this binary runs
    // (Linux: the systemd unit calls "daemon"). Null on a platform whose instance is something else
    // - the elevated tray app on Windows publishes its address from its own startup - and then
    // there is no "daemon" command to type, which is right: it would have nothing to be.
    public Func<IAppDaemonHost>? CreateDaemonHost { get; init; }
}
