using VpnHood.AppLib.Api.Exceptions;

namespace VpnHood.AppLib.Api.Exceptions.AdExceptions;

public class AdBlockerException(string message) : AdException(message)
{
    public bool IsPrivateDns {
        get => Data["IsPrivateDns"] is true;
        set => Data["IsPrivateDns"] = value;
    }
}