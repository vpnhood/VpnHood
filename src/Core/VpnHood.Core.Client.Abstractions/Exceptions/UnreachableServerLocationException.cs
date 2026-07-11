using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerLocationException(string? message = null, Exception? innerException = null)
    : UnreachableServerException(message, innerException)
{
    public static UnreachableServerLocationException Create(string? serverLocation)
    {
        var isAutoLocation = ServerLocationInfo.IsAutoLocation(serverLocation);
        var msg = isAutoLocation
            ? "There is no reachable server at this moment. Please try again later."
            : $"There is no reachable server at this moment. Please try again later. Location: {serverLocation}";
        
        var ex = new UnreachableServerLocationException(msg);
        ex.Data.Add("ServerLocation", serverLocation);
        ex.Data.Add("IsAutoLocation", ServerLocationInfo.IsAutoLocation(serverLocation));
        return ex;
    }
}
