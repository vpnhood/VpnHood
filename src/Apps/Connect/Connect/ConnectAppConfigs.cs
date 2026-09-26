using System.Reflection;
using VpnHood.AppLib.App.Utils;

namespace VpnHood.App.Connect;

// Connect's settings: what its private appsettings can say (AppConfigs), which this project embeds
// once for every head, and Connect's own keys - the portal and its sign-in, the ad networks' ids,
// the install attribution key. A head reads the ones it uses; none has a value in code.
public class ConnectAppConfigs : AppConfigs
{
    public Uri? PortalBaseUri { get; set; }
    public string? GoogleSignInClientId { get; set; }
    public string? AdMobInterstitialAdUnitId { get; set; }
    public string? AdMobRewardedAdUnitId { get; set; }
    public string? ChartboostAppId { get; set; }
    public string? ChartboostAppSignature { get; set; }
    public string? ChartboostAdLocation { get; set; }
    public string? InmobiAccountId { get; set; }
    public string? InmobiPlacementId { get; set; }
    public string? AppsFlyerDevKey { get; set; }

    // Connect's settings, with the built-in key a head embeds as access_key_default.txt in place of
    // the one the appsettings name. The key is the head's own file, from a secret: only the
    // ad-supported build takes the ad key.
    public static ConnectAppConfigs Load(Assembly headAssembly)
    {
        var appConfigs = AppConfigsLoader.Load<ConnectAppConfigs>();
        var accessKey = AppConfigsLoader.ReadResourceText(headAssembly, "access_key_default.txt");
        if (!string.IsNullOrWhiteSpace(accessKey))
            appConfigs.DefaultAccessKey = accessKey.Trim();

        return appConfigs;
    }
}
