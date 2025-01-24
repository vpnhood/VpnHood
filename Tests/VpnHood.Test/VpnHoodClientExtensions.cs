using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Test;

public static class VpnHoodClientExtensions
{
    public static IClientStat GetRequiredStat(this VpnHoodClient client)
        => client.Stat ?? throw new InvalidOperationException("Session has not been initialized yet");

}