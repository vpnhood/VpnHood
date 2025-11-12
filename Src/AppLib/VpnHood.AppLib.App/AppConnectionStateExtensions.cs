namespace VpnHood.AppLib;

public static class AppConnectionStateExtensions
{
    extension(AppConnectionState connectionState)
    {
        public bool IsIdle()
        {
            return connectionState is AppConnectionState.None;
        }

        public bool CanConnect()
        {
            return connectionState.IsIdle();
        }

        public bool CanDiagnose(bool hasDiagnoseStarted)
        {
            // Check if diagnose is already started
            if (hasDiagnoseStarted)
                return false;

            return connectionState is
                AppConnectionState.ValidatingProxies or
                AppConnectionState.FindingReachableServer or
                AppConnectionState.FindingBestServer or
                AppConnectionState.Connecting or
                AppConnectionState.Connected or
                AppConnectionState.Unstable or
                AppConnectionState.None;
        }

        public bool CanDisconnect()
        {
            return connectionState != AppConnectionState.None &&
                   connectionState != AppConnectionState.Disconnecting;
        }
    }
}