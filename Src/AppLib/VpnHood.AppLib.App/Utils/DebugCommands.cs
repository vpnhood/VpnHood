namespace VpnHood.AppLib.Utils;

public static class DebugCommands
{
    public const string DropUdp = "/drop-udp";
    public const string NoTcpReuse = "/no-tcp-reuse";
    public const string KillSpaServer = "/kill-spa-server";
    public const string Verbose = "/verbose";
    public const string NullCapture = "/null-capture";
    public const string CaptureContext = "/capture-context";

    public static string[] All => [
        CaptureContext,
        DropUdp, 
        KillSpaServer, 
        NoTcpReuse, 
        NullCapture,
        Verbose
    ];
}