using System.Net;
using VpnHood.AppLib.Dtos;
using VpnHood.Core.IpLocations;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test;

public static class VpnHoodAppExtensions
{
    extension(VpnHoodApp app)
    {
        public AppSessionStatus GetSessionStatus()
        {
            app.ForceUpdateState().Wait();
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
            app.SettingsService.Settings.ClientIpLocation = new IpLocation {
                IpAddress = IPAddress.Parse("1.2.3.4"), // Dummy IP address
                CountryCode = countryCode,
                CountryName = VhUtils.TryGetCountryName(countryCode) ?? "Unknown",
                CityName = null,
                RegionName = null
            };
        }
    }
}