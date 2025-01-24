using VpnHood.Core.Client;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    public static IClientStat GetRequiredStat(this VpnHoodApp app)
        => app.State.Stat ?? throw new InvalidOperationException("Session has not been initialized yet");
}