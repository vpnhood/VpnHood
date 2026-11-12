namespace VpnHood.AppUi.Hosting.Desktop.Exceptions;

/// <summary>
/// The exception that is thrown when whoever asked may not use the app on this computer, as the service answers, or as
/// the caller's own check finds before it asks.
/// </summary>
/// <remarks>The message is the sentence a command prints as it is.</remarks>
/// <param name="message">The sentence.</param>
public class DaemonRefusedException(string message) : Exception(message);
