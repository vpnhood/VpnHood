namespace VpnHood.AppLib.AvaloniaUI.Resources;

// The UI's words, English only for now, each the web UI's English for the same key (en.json). A
// catalogue of its own rather than the web UI's locale files: those are compiled into the SPA's
// chunks and never travel as JSON, so a native UI cannot read them at runtime. Before this UI grows
// past a few pages, the two need one source (TV plan, Phase 4); until then this is the file the
// translator has to learn.
public static class Strings
{
    // connection states, as the web UI names them (VpnHoodAppData.connectionStateText)
    public const string Connect = "Connect";
    public const string Disconnect = "Disconnect";
    public const string Disconnected = "Disconnected";
    public const string Connecting = "Connecting";
    public const string Connected = "Connected";
    public const string Disconnecting = "Disconnecting";
    public const string Initializing = "Initializing";
    public const string Waiting = "Waiting";
    public const string Diagnosing = "Diagnosing";
    public const string ValidatingProxies = "Validating proxies";
    public const string LoadingAd = "Loading Ad";
    public const string FindingNetwork = "Finding network";
    public const string FindingBestServer = "Finding best server";
    public const string Unstable = "Unstable";
    public const string Cancel = "Cancel";
    public const string StopDiagnosing = "Stop Diagnosing";

    // home
    public const string Statistics = "Statistics";
    public const string Mbps = "Mbps";
    public const string Of = "of";
    public const string VersionAbbreviation = "v";

    // locations
    public const string Location = "Location";
    public const string NoLocation = "No Location";
    public const string AutoSelect = "Auto Select";
    public const string Auto = "Auto";
    public const string Servers = "Servers";
    public const string FreeLocations = "Free Locations";
    public const string PremiumLocations = "Premium Locations";
    public const string Fastest = "Fastest";
    public const string Recommended = "Recommended";
    public const string Active = "Active";
    public const string AlreadyConnectedToLocation = "You are already connected to the selected location.";

    // a TV's settings row and pairing page
    public const string Settings = "Settings";
    public const string OnYourPhone = "On your phone";
    public const string RemoteAccess = "Remote Access";
    public const string RemoteAccessDesc =
        "Manage VpnHood from your phone or computer: scan this code, or type the address into a browser on the same network.";
    public const string RemoteAccessKeepOpen =
        "Keep this open while you make changes. Remote access ends when you press Done or go back.";
    public const string RemoteAccessNoDevice = "No device connected yet.";
    public const string RemoteAccessConnectedFrom = "Connected from {0}.";
    public const string RemoteAccessStarting = "Starting…";
    public const string Done = "Done";
}
