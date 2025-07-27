using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Common.Tokens;

public static class ServerTokenExtensions
{
    public static bool IsTokenUpdated(this ServerToken serverToken, ServerToken newServerToken)
    {
        // create first server token by removing its created time
        var serverToken1 = JsonUtils.JsonClone(serverToken);
        serverToken1.CreatedTime = DateTime.MinValue;

        // create second server token by removing its created time
        var serverToken2 = JsonUtils.JsonClone(newServerToken);
        serverToken2.CreatedTime = DateTime.MinValue;

        // compare
        if (JsonSerializer.Serialize(serverToken1) == JsonSerializer.Serialize(serverToken2))
            return false;

        // if token are not equal, check if new token CreatedTime is newer or equal.
        // If created time is equal assume new token is updated because there is change in token.
        return newServerToken.CreatedTime >= serverToken.CreatedTime;
    }
}