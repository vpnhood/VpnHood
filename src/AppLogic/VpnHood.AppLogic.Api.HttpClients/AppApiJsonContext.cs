using System.Text.Json.Serialization;
using VpnHood.AppLogic.Api.App;
using VpnHood.AppLogic.Api.Accounts;
using VpnHood.AppLogic.Api.Billing;
using VpnHood.AppLogic.Api.VpnProfiles;
using VpnHood.AppLogic.Api.Premium;
using VpnHood.AppLogic.Api.Proxies;
using VpnHood.AppLogic.Api.Settings;
using VpnHood.AppLogic.Api.SplitTunneling;
using VpnHood.AppLogic.Api.Device;
using VpnHood.AppLogic.Api.Updaters;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.AppLogic.Api.HttpClients;

// Every type the HTTP clients read or write, generated ahead of time: the browser is published
// trimmed, and a type the trimmer cannot see is a type it removes. camelCase, as the server speaks.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppInfo))]
[JsonSerializable(typeof(ConfigParams))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(SplitIpsViaApp))]
[JsonSerializable(typeof(SplitIpsViaDevice))]
[JsonSerializable(typeof(SplitDomains))]
[JsonSerializable(typeof(IReadOnlyList<DeviceAppInfo>))]
[JsonSerializable(typeof(AppUserReview))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RemoteAccessState))]
[JsonSerializable(typeof(VersionCheckResult))]
[JsonSerializable(typeof(VpnProfileInfo))]
[JsonSerializable(typeof(VpnProfileUpdateParams))]
[JsonSerializable(typeof(AppPurchaseOptions))]
[JsonSerializable(typeof(Account))]
[JsonSerializable(typeof(SignInOptions))]
[JsonSerializable(typeof(SignInResult))]
[JsonSerializable(typeof(IReadOnlyList<SubscriptionPlan>))]
[JsonSerializable(typeof(PurchaseParams))]
[JsonSerializable(typeof(AppProxyEndPointInfo))]
[JsonSerializable(typeof(ListResult<AppProxyEndPointInfo>))]
[JsonSerializable(typeof(ProxyEndPoint))]
[JsonSerializable(typeof(ProxyEndPointDefaults))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal sealed partial class AppApiJsonContext : JsonSerializerContext;
