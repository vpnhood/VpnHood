namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown when an operation needs the app's UI context, as to show an ad, and there is none.
/// </summary>
public class UiContextNotAvailableException() : Exception("UiContext is not available.");