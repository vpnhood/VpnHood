using VpnHood.Common.Tokens;

namespace VpnHood.Client.Exceptions;

public class UnreachableServer(string? serverLocation = null)
    : Exception(
        ServerLocationInfo.IsAutoLocation(serverLocation)
            ? "There is no reachable server at this moment. Please try again later."
            : $"There is no reachable server at this moment. Please try again later. Location: {serverLocation}");