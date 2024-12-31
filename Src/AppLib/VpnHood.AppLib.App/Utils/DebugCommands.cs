namespace VpnHood.AppLib.Utils;

public static class DebugCommands
{
    public const string DropUdp = "/drop-udp";
    public const string NoTcpReuse = "/no-tcp-reuse";
    public const string KillSpaServer = "/kill-spa-server";
    public const string Verbose = "/verbose";
    public const string NullCapture = "/null-capture";
    public const string CaptureContext = "/capture-context";
    public const string FullLog = "/full-log";

    public static string[] All => [
        CaptureContext,
        DropUdp, 
        FullLog,
        KillSpaServer, 
        NoTcpReuse, 
        NullCapture,
        Verbose
    ];
}