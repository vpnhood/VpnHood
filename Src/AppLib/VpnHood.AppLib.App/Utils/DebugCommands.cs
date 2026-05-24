namespace VpnHood.AppLib.Utils;

public static class DebugCommands
{
    public const string CaptureContext = "/capture-context";
    public const string DropUdp = "/drop-udp";
    public const string KillSpaServer = "/kill-spa-server";
    public const string LogDebug = "/log:debug";
    public const string LogTrace = "/log:trace";
    public const string NullCapture = "/null-capture";
    public const string NoChannelReuse = "/no-channel-reuse";
    public const string WinDivert = "/windivert";
    public const string UserReview = "/user-review";
    public const string RemoteAccess = "/remote-access";
    public const string DisableWebSocket = "/disable-websocket";
    public const string OsTcpStack = "/os-tcp-stack";

    public static string[] All => [
        CaptureContext,
        DropUdp,
        LogDebug,
        LogTrace,
        KillSpaServer,
        NoChannelReuse,
        NullCapture,
        UserReview,
        WinDivert,
        RemoteAccess,
        DisableWebSocket,
        OsTcpStack
    ];
}