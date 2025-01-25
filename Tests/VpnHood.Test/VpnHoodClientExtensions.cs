using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Test;

public static class VpnHoodClientExtensions
{
    public static ISessionStatus GetSessionStatus(this VpnHoodClient client)
        => client.ConnectionInfo.SessionStatus ?? throw new InvalidOperationException("Session has not been initialized yet");

}