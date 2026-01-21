using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.WebServer.Api;

public interface IAppController
{
    Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode, CancellationToken cancellationToken);
    Task<AppData> Configure(ConfigParams configParams, CancellationToken cancellationToken);
    Task<AppData> GetConfig(CancellationToken cancellationToken);
    Task<SplitByIps> GetSplitByIps(CancellationToken cancellationToken);
    Task SetSplitByIps(SplitByIps value, CancellationToken cancellationToken);
    Task<AppState> GetState(CancellationToken cancellationToken);
    Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);
    Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);
    Task Disconnect(CancellationToken cancellationToken);
    Task ClearLastError(CancellationToken cancellationToken);
    Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken);
    Task<string> Log(CancellationToken cancellationToken);
    Task<byte[]> PromotionImage(CancellationToken cancellationToken);
    Task<DeviceAppInfo[]> GetInstalledApps(CancellationToken cancellationToken);
    Task VersionCheck(CancellationToken cancellationToken);
    Task VersionCheckPostpone(CancellationToken cancellationToken);
    Task ExtendByRewardedAd(CancellationToken cancellationToken);
    Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken);
    Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken);
    Task<CountryInfo[]> GetSupportedSplitByCountries(CancellationToken cancellationToken);
    Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken);
    Task InternalAdError(string errorMessage, CancellationToken cancellationToken);
    Task RemovePremium(Guid profileId, CancellationToken cancellationToken);
}