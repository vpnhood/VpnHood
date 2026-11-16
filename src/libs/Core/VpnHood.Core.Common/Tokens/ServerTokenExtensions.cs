using System.Text.Json;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Provides the comparison of server tokens.
/// </summary>
public static class ServerTokenExtensions
{
    /// <summary>Determines whether a server token is an update of another.</summary>
    /// <param name="serverToken">The current token.</param>
    /// <param name="newServerToken">The token that may replace it.</param>
    /// <returns>
    /// <see langword="true"/> if the new token differs from the current one other than in its creation time, and was
    /// made no earlier; otherwise, <see langword="false"/>.
    /// </returns>
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