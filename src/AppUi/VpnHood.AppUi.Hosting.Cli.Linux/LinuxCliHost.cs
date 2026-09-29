namespace VpnHood.AppUi.Hosting.Cli.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (CliPlatform) joined to the
// product facts the head answers (CliInitParams). The same synchronous call as every desktop
// host's, run on the head's main thread (CliHost.Run).
public static class LinuxCliHost
{
    public static int Run(string[] args, CliInitParams initParams)
    {
        var paths = new LinuxCliPaths(initParams.AppId);
        // No tray keeps a hidden window, so a closed one is gone; the installer registers the unit;
        // and systemd's signal stops the daemon.
        var platform = new CliPlatform {
            Paths = paths,
            Instance = new LinuxInstanceController(paths),
            Channel = new LinuxDaemonChannel(paths),
            PeerCheck = new LinuxLoopbackPeerCheck(),
            DaemonHostFactory = new LinuxDaemonHostFactory(initParams, paths)
        };

        return CliHost.Run(args, initParams, platform);
    }
}
