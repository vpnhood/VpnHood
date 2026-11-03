using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.App;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    extension(VpnHoodApp app)
    {
        public AppSessionStatus GetSessionStatus(CancellationToken cancellationToken)
        {
            app.ForceUpdateState(cancellationToken).Wait(cancellationToken);
            return app.State.SessionStatus ??
                   throw new InvalidOperationException("Session has not been initialized yet");
        }

        public async Task<AppSessionStatus> GetSessionStatusAsync(CancellationToken cancellationToken)
        {
            await app.ForceUpdateState(cancellationToken);
            return app.State.SessionStatus ??
                   throw new InvalidOperationException("Session has not been initialized yet");
        }


        public Task WaitForState(AppConnectionState connectionSate, CancellationToken cancellationToken,
            int timeout = 5000)
        {
            return VhTestUtil.AssertEqualsWait(connectionSate, () => app.State.ConnectionState,
                "App state didn't reach the expected value.", timeout, cancellationToken: cancellationToken);
        }

        public Task Connect(Guid? vpnProfileId,
            CancellationToken cancellationToken,
            ConnectPlanId planId = ConnectPlanId.Normal,
            bool diagnose = false)
        {
            // the window as the API passes it: the one the test has set, if any
            return app.Connect(AppUiContext.Context, new ConnectOptions {
                VpnProfileId = vpnProfileId,
                PlanId = planId,
                Diagnose = diagnose
            }, cancellationToken);
        }

        public void UpdateClientCountry(string countryCode)
        {
            // VpnProfileService detects the region change itself and rebuilds its cached info
            AppRegionInfo.CurrentRegion = new RegionInfo(countryCode);
        }
    }
}