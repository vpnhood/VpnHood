using VpnHood.AppLib.Dtos;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    public static AppSessionStatus GetSessionStatus(this VpnHoodApp app)
        => app.State.SessionStatus ?? throw new InvalidOperationException("Session has not been initialized yet");
}