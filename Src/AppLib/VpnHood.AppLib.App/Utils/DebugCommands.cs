namespace VpnHood.AppLib.Utils;

public static class DebugCommands
{
    public const string CaptureContext = "/capture-context";
    public const string DropUdp = "/drop-udp";
    public const string KillSpaServer = "/kill-spa-server";
    public const string UseTcpOverTun = "/tcp-over-tun";
    public const string LogDebug = "/log:debug";
    public const string LogTrace = "/log:trace";
    public const string NullCapture = "/null-capture";
    public const string NoTcpReuse = "/no-tcp-reuse";
    public const string WinDivert = "/windivert";

    public static string[] All => [
        CaptureContext,
        DropUdp,
        LogDebug,
        LogTrace,
        KillSpaServer,
        UseTcpOverTun,
        NoTcpReuse,
        NullCapture,
        WinDivert
    ];
}