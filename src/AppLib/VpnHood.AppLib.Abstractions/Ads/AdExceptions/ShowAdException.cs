namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class ShowAdException(string message, Exception? innerException = null)
    : AdException(message, innerException)
{
    public string? AdNetworkName { get; set; }
}
