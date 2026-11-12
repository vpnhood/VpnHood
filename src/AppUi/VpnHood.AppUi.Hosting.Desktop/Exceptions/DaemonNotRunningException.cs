namespace VpnHood.AppUi.Hosting.Desktop.Exceptions;

/// <summary>
/// The exception that is thrown when the app's service cannot be reached: no address is published, or the service's
/// answer went away.
/// </summary>
/// <remarks>The message is the platform's own sentence on how to start the service.</remarks>
/// <param name="notRunningHint">The platform's sentence on how to start the service.</param>
public class DaemonNotRunningException(string notRunningHint) : InvalidOperationException(notRunningHint);
