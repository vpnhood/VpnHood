using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.AppLib.Contracts.Device;
using VpnHood.AppLib.Contracts.Proxies;
using VpnHood.AppLib.Contracts.Sessions;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;
using CoreDeviceAppInfo = VpnHood.Core.Client.Devices.DeviceAppInfo;

using VpnHood.AppLib.Services.Countries;

namespace VpnHood.AppLib.DtoConverters;

public static class AppDtoConverterExtensions
{
    public static DeviceAppInfo ToAppDto(this CoreDeviceAppInfo deviceAppInfo)
    {
        return new DeviceAppInfo {
            AppId = deviceAppInfo.AppId,
            AppName = deviceAppInfo.AppName,
            IconPng = deviceAppInfo.IconPng
        };
    }

    public static AppConnectorStat ToAppDto(this ConnectorStatus connectorStatus)
    {
        return new AppConnectorStat {
            FreeConnectionCount = connectorStatus.FreeConnectionCount,
            ReusedConnectionFailedCount = connectorStatus.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStatus.ReusedConnectionSucceededCount,
            CreatedConnectionCount = connectorStatus.CreatedConnectionCount,
            RequestCount = connectorStatus.RequestCount
        };
    }

    public static AppSessionInfo ToAppDto(this SessionInfo sessionInfo)
    {
        return new AppSessionInfo {
            AccessInfo = sessionInfo.AccessInfo?.ToAppDto(),
            DnsConfig = sessionInfo.DnsConfig.ToAppDto(),
            IsLocalNetworkAllowed = sessionInfo.IsLocalNetworkAllowed,
            IsTrafficSplitByServer = sessionInfo.IsTrafficSplitByServer,
            IsIpV6SupportedByServer = sessionInfo.IsIpV6SupportedByServer,
            ServerLocationInfo = sessionInfo.ServerLocationInfo?.ToAppDto(),
            ServerVersion = sessionInfo.ServerVersion,
            IsPremiumSession = sessionInfo.IsPremiumSession,
            SuppressedTo = sessionInfo.SuppressedTo.ToAppDto(),
            ClientPublicIpAddress = sessionInfo.ClientPublicIpAddress,
            CreatedTime = sessionInfo.CreatedTime,
            ChannelProtocols = [.. sessionInfo.ChannelProtocols.Select(x => x.ToAppDto())],
            IsTcpProxySupported = sessionInfo.IsTcpProxySupported,
            IsTcpPacketSupported = sessionInfo.IsTcpPacketSupported
        };
    }

    public static AppSessionStatus ToAppDto(this SessionStatus sessionStatus, bool canExtendByRewardedAd)
    {
        return new AppSessionStatus {
            ConnectorStat = sessionStatus.ConnectorStatus.ToAppDto(),
            Speed = sessionStatus.Speed.ToAppDto(),
            SessionTraffic = sessionStatus.SessionTraffic.ToAppDto(),
            SessionSplitTraffic = sessionStatus.SessionSplitTraffic.ToAppDto(),
            CycleTraffic = sessionStatus.CycleTraffic.ToAppDto(),
            TotalTraffic = sessionStatus.TotalTraffic.ToAppDto(),
            StreamTunnelledCount = sessionStatus.StreamTunnelledCount,
            StreamPassthruCount = sessionStatus.StreamPassthruCount,
            PacketChannelCount = sessionStatus.PacketChannelCount,
            UnstableCount = sessionStatus.UnstableCount,
            WaitingCount = sessionStatus.WaitingCount,
            CanExtendByRewardedAd = canExtendByRewardedAd,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            ActiveClientCount = sessionStatus.ActiveClientCount,
            ChannelProtocol = sessionStatus.ChannelProtocol.ToAppDto(),
            IsTcpProxy = sessionStatus.IsTcpProxy,
            CanChangeTcpProxy = sessionStatus.CanChangeTcpProxy,
            IsDropQuic = sessionStatus.IsDropQuic
        };
    }

    public static ApiError ToAppDto(this ApiError apiError)
    {
        apiError = (ApiError)apiError.Clone();

        // remove sensitive info like access key 
        apiError.Data.Remove(nameof(SessionResponse));

        return apiError;
    }

    public static AppProxyConnectorStatus ToAppDto(this ProxyConnectorStatus status)
    {
        return new AppProxyConnectorStatus {
            SessionStatus = status.SessionStatus.ToAppDto(),
            SucceededServerCount = status.SucceededServerCount,
            FailedServerCount = status.FailedServerCount,
            UnknownServerCount = status.UnknownServerCount,
            DisabledServerCount = status.DisabledServerCount
        };
    }

    // The shape a UI reads a location as, with the country name already in the language the app
    // speaks - the contract carries names, it never looks one up.
    public static CurrentServerLocationInfo ToAppDto(this ServerLocationInfo value, bool hasMultipleRegions = false)
    {
        return new CurrentServerLocationInfo {
            CountryCode = value.CountryCode,
            RegionName = value.RegionName,
            ServerLocation = value.ServerLocation,
            CountryName = value.CountryName,
            IsAuto = value.IsAuto,
            HasRegion = value.HasRegion,
            HasMultipleRegions = hasMultipleRegions,
            TranslatedCountryName = TranslatedNameOf(value.CountryCode, value.CountryName)
        };
    }

    // The same location, already in the contract's shape: the profile's list is built once and the
    // state points at one of its entries, so nothing is re-derived and no engine type is needed.
    public static CurrentServerLocationInfo ToAppDto(this ServerLocationItem value, bool hasMultipleRegions = false)
    {
        return new CurrentServerLocationInfo {
            CountryCode = value.CountryCode,
            RegionName = value.RegionName,
            ServerLocation = value.ServerLocation,
            CountryName = value.CountryName,
            IsAuto = value.IsAuto,
            HasRegion = value.HasRegion,
            HasMultipleRegions = hasMultipleRegions,
            TranslatedCountryName = TranslatedNameOf(value.CountryCode, value.CountryName)
        };
    }

    private static string TranslatedNameOf(string countryCode, string countryName)
    {
        return countryCode == ServerLocationInfo.AutoCountryCode
            ? countryName
            : AppCountryInfo.TryGet(countryCode)?.TranslatedName ?? countryName;
    }

    // What the device can open or ask for, read off the providers the head supplied. The shape is
    // the contract's; asking a provider is the app's.
    public static DeviceIntentFeatures ToIntentFeatures(this IDeviceUiProvider? uiProvider,
        IAppUserReviewProvider? userReviewProvider)
    {
        return new DeviceIntentFeatures {
            IsUserReviewSupported = userReviewProvider != null,
            IsWebBrowserSupported = uiProvider?.IsWebBrowserSupported ?? false,
            IsQuickLaunchSupported = uiProvider?.IsQuickLaunchSupported ?? false,
            IsRequestQuickLaunchSupported = uiProvider?.IsRequestQuickLaunchSupported ?? false,
            IsRequestNotificationSupported = uiProvider?.IsRequestNotificationSupported ?? false,
            IsPrivateDnsSettingsSupported = uiProvider?.IsPrivateDnsSettingsSupported ?? false,
            IsKillSwitchSettingsSupported = uiProvider?.IsKillSwitchSettingsSupported ?? false,
            IsAlwaysOnSettingsSupported = uiProvider?.IsAlwaysOnSettingsSupported ?? false,
            IsSettingsSupported = uiProvider?.IsSettingsSupported ?? false,
            IsAppSettingsSupported = uiProvider?.IsAppSettingsSupported ?? false,
            IsAppNotificationSettingsSupported = uiProvider?.IsAppNotificationSettingsSupported ?? false
        };
    }
}
