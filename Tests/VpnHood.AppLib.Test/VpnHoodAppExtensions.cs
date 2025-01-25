using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    public static ISessionStatus GetSessionStatus(this VpnHoodApp app)
        => app.State.SessionStatus ?? throw new InvalidOperationException("Session has not been initialized yet");
}