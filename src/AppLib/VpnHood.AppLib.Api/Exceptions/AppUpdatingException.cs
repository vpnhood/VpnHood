namespace VpnHood.AppLib.Api.Exceptions;

// A connect while the app installs its update (AppConnectionState.Updating), whose install stops the app in a moment
public class AppUpdatingException()
    : Exception("An update is being installed. Connect again once the app has restarted.");
