namespace VpnHood.AppLib.AvaloniaUI;

// Which product a head is, the one question both the palette and the flow ask. The web UI asks it
// of AppFeatures.UiName too (isConnectApp in services/VpnHoodApp.ts), and answers "the client" for
// a head that names no product, as every client head does - only the connect heads set it.
public static class AppProduct
{
    public const string ConnectUiName = "VpnHoodConnect";

    public static bool IsConnect(string? uiName)
    {
        return uiName == ConnectUiName;
    }

    // Connect ships one built-in profile and calls its list "location"; the client keeps a list of
    // servers, each with locations of its own, and calls it "server". The web UI's
    // isSingleProfileMode, which is that same question under a name that says what it changes.
    public static bool IsSingleProfileMode(string? uiName)
    {
        return IsConnect(uiName);
    }
}
