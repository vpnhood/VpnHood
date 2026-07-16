using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    extension(VpnHoodApp app)
    {
        public AppSessionStatus GetSessionStatus(CancellationToken cancellationToken = default)
        {
            app.ForceUpdateState(cancellationToken).Wait(cancellationToken);
            return app.State.SessionStatus ??
                   throw new InvalidOperationException("Session has not been initialized yet");
        }

        public async Task<AppSessionStatus> GetSessionStatusAsync(CancellationToken cancellationToken = default)
        {
            await app.ForceUpdateState(cancellationToken);
            return app.State.SessionStatus ??
                   throw new InvalidOperationException("Session has not been initialized yet");
        }


        public Task WaitForState(AppConnectionState connectionSate, int timeout = 5000)
        {
            return VhTestUtil.AssertEqualsWait(connectionSate, () => app.State.ConnectionState,
                "App state didn't reach the expected value.", timeout);
        }

        public Task Connect(Guid? clientProfileId,
            ConnectPlanId planId = ConnectPlanId.Normal,
            bool diagnose = false,
            CancellationToken cancellationToken = default)
        {
            return app.Connect(new ConnectOptions {
                ClientProfileId = clientProfileId,
                PlanId = planId,
                Diagnose = diagnose
            }, cancellationToken);
        }

        public void UpdateClientCountry(string countryCode)
        {
            // ClientProfileService detects the region change itself and rebuilds its cached info
            AppRegionInfo.CurrentRegion = new RegionInfo(countryCode);
        }
    }
}