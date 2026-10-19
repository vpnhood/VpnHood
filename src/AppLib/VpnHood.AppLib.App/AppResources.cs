using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.App;

// What the engine itself shows: its own words. The window's look - its size, its colours, the chrome
// around it, the taskbar badge - is the UI's, which the engine never draws.
public class AppResources
{
    // Each word is resolved on read - the provider, or this library's own English - so a language
    // chosen while the app runs is the one shown.
    public IAppStrings Strings { get; }

    // The UI's words, pushed through ConfigParams.Strings; null, and every word is the resx's.
    public IStringProvider? StringProvider { get; set; }

    public AppResources()
    {
        Strings = new AppStrings(this);
    }

    private string? GetString(string key)
    {
        // the app's language, not this thread's: VpnHoodApp writes it on the thread that initializes it
        var culture = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
        return StringProvider?.GetString(culture, key);
    }

    // The connection's states in the UI's words, keyed as its main page keys them
    // (Strings.ConnectionStateText); a state it gave no word for is left out.
    public IReadOnlyDictionary<ClientState, string> GetClientStateTexts()
    {
        var texts = new Dictionary<ClientState, string>();
        foreach (var (clientState, key) in ClientStateKeys)
            if (GetString(key) is { } text)
                texts[clientState] = text;

        return texts;
    }

    private static readonly (ClientState, string)[] ClientStateKeys = [
        (ClientState.Initializing, "INITIALIZING"),
        (ClientState.ValidatingProxies, "VALIDATING_PROXIES"),
        (ClientState.FindingReachableServer, "FINDING_NETWORK"),
        (ClientState.FindingBestServer, "FINDING_BEST_SERVER"),
        (ClientState.Connecting, "CONNECTING"),
        (ClientState.Connected, "CONNECTED"),
        (ClientState.Unstable, "UNSTABLE"),
        (ClientState.Waiting, "WAITING"),
        (ClientState.WaitingForAd, "LOADING_AD"),
        (ClientState.Disconnecting, "DISCONNECTING"),
        (ClientState.Disposed, "DISCONNECTED")
    ];

    // Each word is looked up when read: the provider, then Resources.resx.
    internal class AppStrings(AppResources resources) : IAppStrings
    {
        public string Manage => resources.GetString("MANAGE") ?? Resources.Manage;
        public string Disconnect => resources.GetString("DISCONNECT") ?? Resources.Disconnect;
        public string MsgAccessKeyAdded => resources.GetString("MSG_ACCESS_KEY_ADDED") ?? Resources.MsgAccessKeyAdded;
        public string MsgAccessKeyUpdated => resources.GetString("MSG_ACCESS_KEY_UPDATED") ?? Resources.MsgAccessKeyUpdated;
        public string MsgCantReadAccessKey => resources.GetString("MSG_CANT_READ_ACCESS_KEY") ?? Resources.MsgCantReadAccessKey;
        public string MsgUnsupportedContent => resources.GetString("MSG_UNSUPPORTED_CONTENT") ?? Resources.MsgUnsupportedContent;
        public string UpdateReadyToInstall => resources.GetString("UPDATE_READY_TO_INSTALL") ?? Resources.UpdateReadyToInstall;
    }
}