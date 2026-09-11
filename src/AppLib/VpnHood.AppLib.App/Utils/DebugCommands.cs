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

    // Reports the device as a TV (AppFeatures.IsTv), so the TV layout and its reductions can be
    // worked on from a phone or a desktop. Applies at the next launch, like every feature. Only
    // what keys off AppFeatures.IsTv follows it; the device-side gating in AndroidDeviceUiProvider
    // still reads the real device.
    public const string TvMode = "/tv-mode";

    // Read by the SPA only: it reveals the Starlink Tools page in Settings. The app itself never
    // acts on it.
    public const string Starlink = "/starlink";

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
        OsTcpStack,
        TvMode,
        Starlink
    ];
}