namespace VpnHood.AppLib.Utils;

public static class DebugCommands
{
    public const string CaptureContext = "/capture-context";
    public const string DropUdp = "/drop-udp";
    public const string FullLog = "/full-log";
    public const string KillSpaServer = "/kill-spa-server";
    public const string UseTcpOverTun = "/tcp-over-tun";
    public const string Verbose = "/verbose";
    public const string NullCapture = "/null-capture";
    public const string NoTcpReuse = "/no-tcp-reuse";

    public static string[] All => [
        CaptureContext,
        DropUdp, 
        FullLog,
        KillSpaServer,
        UseTcpOverTun,
        NoTcpReuse, 
        NullCapture,
        Verbose
    ];
}