namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>
/// Represents the error of a connect while the app installs its update (<see cref="App.AppConnectionState.Updating"/>),
/// whose install stops the app in a moment.
/// </summary>
public class AppUpdatingException()
    : Exception("An update is being installed. Connect again once the app has restarted.");
