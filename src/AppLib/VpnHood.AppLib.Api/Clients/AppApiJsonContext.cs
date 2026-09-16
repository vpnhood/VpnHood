using System.Text.Json.Serialization;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.AppLib.Contracts.Countries;
using VpnHood.AppLib.Contracts.Premium;
using VpnHood.AppLib.Contracts.Proxies;
using VpnHood.AppLib.Contracts.Settings;
using VpnHood.AppLib.Contracts.SplitTunneling;
using VpnHood.Core.Client.Devices;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Generics;

namespace VpnHood.AppLib.Api.Clients;

// Every type the HTTP clients read or write, generated ahead of time: the browser is published
// trimmed, and a type the trimmer cannot see is a type it removes. camelCase, as the server speaks.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppData))]
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
