namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class ShowAdNoUiException(string? message = null, Exception? innerException = null)
    : ShowAdException(message ?? DefaultMessage, innerException)
{
    private const string DefaultMessage = "Could not show any ad because the app window was not open.";
}
