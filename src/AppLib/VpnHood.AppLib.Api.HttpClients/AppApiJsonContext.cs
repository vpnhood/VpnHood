using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Countries;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Device;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.AppLib.Api.HttpClients;

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
[JsonSerializable(typeof(CountryInfo[]))]
[JsonSerializable(typeof(RemoteAccessState))]
[JsonSerializable(typeof(ClientProfileInfo))]
[JsonSerializable(typeof(ClientProfileUpdateParams))]
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
