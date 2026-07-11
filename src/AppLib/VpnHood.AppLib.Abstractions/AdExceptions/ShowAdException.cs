namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class ShowAdException(string message, Exception? innerException = null)
    : AdException(message, innerException)
{
    public string? AdNetworkName { get; set; }
}
