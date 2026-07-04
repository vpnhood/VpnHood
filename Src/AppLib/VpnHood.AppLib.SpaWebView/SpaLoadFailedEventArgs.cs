namespace VpnHood.AppLib.SpaWebView;

public class SpaLoadFailedEventArgs(bool duringInitialConnect) : EventArgs
{
    // True when the failure was establishing the very first connection to the loopback server
    // (e.g. an iOS provisional-navigation failure). That is strong evidence the listener is dead
    // even if it still reports it is up, so the host force-restarts the server rather than only
    // reloading the page.
    public bool DuringInitialConnect { get; } = duringInitialConnect;
}
