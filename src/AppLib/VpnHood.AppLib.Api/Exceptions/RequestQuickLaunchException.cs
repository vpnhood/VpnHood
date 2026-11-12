namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>Represents the error of a Quick Launch the system refused at once.</summary>
/// <param name="message">The error's message.</param>
public class RequestQuickLaunchException(string message) : Exception(message);