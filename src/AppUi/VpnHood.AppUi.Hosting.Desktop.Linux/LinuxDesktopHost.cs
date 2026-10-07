namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// A Linux head's whole entry point: the machine facts Linux answers (LinuxDesktopPlatform) joined to
// the product facts the head answers (DesktopInitParams). The same synchronous call as every desktop
// host's, run on the head's main thread (DesktopHost.Run).
public static class LinuxDesktopHost
{
    public static int Run(string[] args, DesktopInitParams initParams)
    {
        return DesktopHost.Run(args, initParams, new LinuxDesktopPlatform(initParams));
    }
}
