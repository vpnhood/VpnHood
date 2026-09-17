namespace VpnHood.AppLib.Api.App;

// The words the app draws where no UI can reach: a system tray menu, a notification, a toast the
// platform raises. They are a contract because a UI pushes its own translations in at configure
// time (ConfigParams.Strings) - the app speaks the language the UI speaks, without the app owning
// a translation file. The defaults are AppResources' to fill, from the library's own resources;
// nothing else constructs this.
public class AppStrings
{
    public string Disconnect { get; set; } = string.Empty;
    public string Connect { get; set; } = string.Empty;
    public string Disconnected { get; set; } = string.Empty;
    public string Exit { get; set; } = string.Empty;
    public string Manage { get; set; } = string.Empty;
    public string MsgAccessKeyAdded { get; set; } = string.Empty;
    public string MsgAccessKeyUpdated { get; set; } = string.Empty;
    public string MsgCantReadAccessKey { get; set; } = string.Empty;
    public string MsgUnsupportedContent { get; set; } = string.Empty;
    public string Open { get; set; } = string.Empty;
    public string OpenInBrowser { get; set; } = string.Empty;
}
