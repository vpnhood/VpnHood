namespace VpnHood.AppLib;

public static class AppConnectionStateExtensions
{
    public static bool IsIdle(this AppConnectionState connectionState)
    {
        return connectionState is AppConnectionState.None;
    }

    public static bool CanConnect(this AppConnectionState connectionState)
    {
        return connectionState.IsIdle();
    }

    public static bool CanDiagnose(this AppConnectionState connectionState, bool hasDiagnoseStarted)
    {
        // Check if diagnose is already started
        if (hasDiagnoseStarted)
            return false;

        return connectionState is
            AppConnectionState.Connected or
            AppConnectionState.Connecting or
            AppConnectionState.Unstable or
            AppConnectionState.None;
    }

    public static bool CanDisconnect(this AppConnectionState connectionState)
    {
        return connectionState != AppConnectionState.None &&
               connectionState != AppConnectionState.Disconnecting;
    }
}