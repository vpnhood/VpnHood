using VpnHood.AppLib.Abstractions.AdExceptions;

namespace VpnHood.AppLib.Exceptions;

public class AdBlockerException(string message) : AdException(message)
{
    public bool IsPrivateDns {
        get => Data["IsPrivateDns"] is true;
        set => Data["IsPrivateDns"] = value;
    }
}
