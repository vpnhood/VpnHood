
namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class AdBlockerException(string message) : AdException(message)
{
    public bool IsPrivateDns {
        get => Data["IsPrivateDns"] is true;
        set => Data["IsPrivateDns"] = value;
    }
}