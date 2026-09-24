using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using CoreMsg = VpnHood.Core.Common.Messaging;
using CoreTokens = VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.App.DtoConverters;

// The session vocabulary the engine speaks and the one the contract publishes. Every enum maps with
// no default arm: these travel to a UI and, for several of them, into settings.json, so a member
// added to the engine must break this file rather than arrive somewhere as a silent fallback.
public static class SessionDtoConverters
{
    public static ChannelProtocol ToAppDto(this CoreMsg.ChannelProtocol protocol)
    {
        return protocol switch {
            CoreMsg.ChannelProtocol.Quic => ChannelProtocol.Quic,
            CoreMsg.ChannelProtocol.Udp => ChannelProtocol.Udp,
            CoreMsg.ChannelProtocol.Tcp => ChannelProtocol.Tcp
        };
    }

    public static CoreMsg.ChannelProtocol ToEngine(this ChannelProtocol protocol)
    {
        return protocol switch {
            ChannelProtocol.Quic => CoreMsg.ChannelProtocol.Quic,
            ChannelProtocol.Udp => CoreMsg.ChannelProtocol.Udp,
            ChannelProtocol.Tcp => CoreMsg.ChannelProtocol.Tcp
        };
    }

    public static SessionSuppressType ToAppDto(this CoreMsg.SessionSuppressType suppressType)
    {
        return suppressType switch {
            CoreMsg.SessionSuppressType.None => SessionSuppressType.None,
            CoreMsg.SessionSuppressType.YourSelf => SessionSuppressType.YourSelf,
            CoreMsg.SessionSuppressType.Other => SessionSuppressType.Other
        };
    }

    public static SessionErrorCode ToAppDto(this CoreMsg.SessionErrorCode errorCode)
    {
        return errorCode switch {
            CoreMsg.SessionErrorCode.Ok => SessionErrorCode.Ok,
            CoreMsg.SessionErrorCode.AccessError => SessionErrorCode.AccessError,
            CoreMsg.SessionErrorCode.PlanRejected => SessionErrorCode.PlanRejected,
            CoreMsg.SessionErrorCode.GeneralError => SessionErrorCode.GeneralError,
            CoreMsg.SessionErrorCode.SessionClosed => SessionErrorCode.SessionClosed,
            CoreMsg.SessionErrorCode.SessionSuppressedBy => SessionErrorCode.SessionSuppressedBy,
            CoreMsg.SessionErrorCode.SessionError => SessionErrorCode.SessionError,
            CoreMsg.SessionErrorCode.SessionExpired => SessionErrorCode.SessionExpired,
            CoreMsg.SessionErrorCode.AccessExpired => SessionErrorCode.AccessExpired,
            CoreMsg.SessionErrorCode.AccessCodeRejected => SessionErrorCode.AccessCodeRejected,
            CoreMsg.SessionErrorCode.AccessLocked => SessionErrorCode.AccessLocked,
            CoreMsg.SessionErrorCode.AccessTrafficOverflow => SessionErrorCode.AccessTrafficOverflow,
            CoreMsg.SessionErrorCode.DailyLimitExceeded => SessionErrorCode.DailyLimitExceeded,
            CoreMsg.SessionErrorCode.NoServerAvailable => SessionErrorCode.NoServerAvailable,
            CoreMsg.SessionErrorCode.PremiumLocation => SessionErrorCode.PremiumLocation,
            CoreMsg.SessionErrorCode.AdError => SessionErrorCode.AdError,
            CoreMsg.SessionErrorCode.RewardedAdRejected => SessionErrorCode.RewardedAdRejected,
            CoreMsg.SessionErrorCode.Maintenance => SessionErrorCode.Maintenance,
            CoreMsg.SessionErrorCode.RedirectHost => SessionErrorCode.RedirectHost,
            CoreMsg.SessionErrorCode.UnsupportedClient => SessionErrorCode.UnsupportedClient,
            CoreMsg.SessionErrorCode.UnsupportedServer => SessionErrorCode.UnsupportedServer
        };
    }

    public static CoreMsg.SessionErrorCode ToEngine(this SessionErrorCode errorCode)
    {
        return errorCode switch {
            SessionErrorCode.Ok => CoreMsg.SessionErrorCode.Ok,
            SessionErrorCode.AccessError => CoreMsg.SessionErrorCode.AccessError,
            SessionErrorCode.PlanRejected => CoreMsg.SessionErrorCode.PlanRejected,
            SessionErrorCode.GeneralError => CoreMsg.SessionErrorCode.GeneralError,
            SessionErrorCode.SessionClosed => CoreMsg.SessionErrorCode.SessionClosed,
            SessionErrorCode.SessionSuppressedBy => CoreMsg.SessionErrorCode.SessionSuppressedBy,
            SessionErrorCode.SessionError => CoreMsg.SessionErrorCode.SessionError,
            SessionErrorCode.SessionExpired => CoreMsg.SessionErrorCode.SessionExpired,
            SessionErrorCode.AccessExpired => CoreMsg.SessionErrorCode.AccessExpired,
            SessionErrorCode.AccessCodeRejected => CoreMsg.SessionErrorCode.AccessCodeRejected,
            SessionErrorCode.AccessLocked => CoreMsg.SessionErrorCode.AccessLocked,
            SessionErrorCode.AccessTrafficOverflow => CoreMsg.SessionErrorCode.AccessTrafficOverflow,
            SessionErrorCode.DailyLimitExceeded => CoreMsg.SessionErrorCode.DailyLimitExceeded,
            SessionErrorCode.NoServerAvailable => CoreMsg.SessionErrorCode.NoServerAvailable,
            SessionErrorCode.PremiumLocation => CoreMsg.SessionErrorCode.PremiumLocation,
            SessionErrorCode.AdError => CoreMsg.SessionErrorCode.AdError,
            SessionErrorCode.RewardedAdRejected => CoreMsg.SessionErrorCode.RewardedAdRejected,
            SessionErrorCode.Maintenance => CoreMsg.SessionErrorCode.Maintenance,
            SessionErrorCode.RedirectHost => CoreMsg.SessionErrorCode.RedirectHost,
            SessionErrorCode.UnsupportedClient => CoreMsg.SessionErrorCode.UnsupportedClient,
            SessionErrorCode.UnsupportedServer => CoreMsg.SessionErrorCode.UnsupportedServer
        };
    }

    public static Traffic ToAppDto(this CoreMsg.Traffic traffic)
    {
        return new Traffic { Sent = traffic.Sent, Received = traffic.Received };
    }

    public static AccessInfo ToAppDto(this CoreMsg.AccessInfo accessInfo)
    {
        return new AccessInfo {
            IsNew = accessInfo.IsNew,
            CreatedTime = accessInfo.CreatedTime,
            LastUsedTime = accessInfo.LastUsedTime,
            ExpirationTime = accessInfo.ExpirationTime,
            IsPremium = accessInfo.IsPremium,
            MaxCycleTraffic = accessInfo.MaxCycleTraffic,
            MaxTotalTraffic = accessInfo.MaxTotalTraffic,
            MaxDeviceCount = accessInfo.MaxDeviceCount,
            MaxSpeedMbps = accessInfo.MaxSpeedMbps?.ToAppDto(),
            DeviceLifeSpan = accessInfo.DeviceLifeSpan,
            DevicesSummary = accessInfo.DevicesSummary?.ToAppDto()
        };
    }

    public static AccessDevicesSummary ToAppDto(this CoreMsg.AccessDevicesSummary summary)
    {
        return new AccessDevicesSummary {
            DeviceCount = summary.DeviceCount,
            HasMoreDevices = summary.HasMoreDevices,
            Devices = summary.Devices?.Select(x => x.ToAppDto()).ToArray()
        };
    }

    public static AccessDevice ToAppDto(this CoreMsg.AccessDevice device)
    {
        return new AccessDevice {
            LastUsedTime = device.LastUsedTime,
            OperatingSystem = device.OperatingSystem,
            IpAddress = device.IpAddress
        };
    }

    public static ConnectPlanId ToAppDto(this CoreTokens.ConnectPlanId planId)
    {
        return planId switch {
            CoreTokens.ConnectPlanId.Normal => ConnectPlanId.Normal,
            CoreTokens.ConnectPlanId.NormalByRewardedAd => ConnectPlanId.NormalByRewardedAd,
            CoreTokens.ConnectPlanId.PremiumByTrial => ConnectPlanId.PremiumByTrial,
            CoreTokens.ConnectPlanId.PremiumByRewardedAd => ConnectPlanId.PremiumByRewardedAd,
            CoreTokens.ConnectPlanId.Status => ConnectPlanId.Status
        };
    }

    public static CoreTokens.ConnectPlanId ToEngine(this ConnectPlanId planId)
    {
        return planId switch {
            ConnectPlanId.Normal => CoreTokens.ConnectPlanId.Normal,
            ConnectPlanId.NormalByRewardedAd => CoreTokens.ConnectPlanId.NormalByRewardedAd,
            ConnectPlanId.PremiumByTrial => CoreTokens.ConnectPlanId.PremiumByTrial,
            ConnectPlanId.PremiumByRewardedAd => CoreTokens.ConnectPlanId.PremiumByRewardedAd,
            ConnectPlanId.Status => CoreTokens.ConnectPlanId.Status
        };
    }

    public static EndPointStrategy ToAppDto(this CoreTokens.EndPointStrategy strategy)
    {
        return strategy switch {
            CoreTokens.EndPointStrategy.Auto => EndPointStrategy.Auto,
            CoreTokens.EndPointStrategy.DnsFirst => EndPointStrategy.DnsFirst,
            CoreTokens.EndPointStrategy.IpFirst => EndPointStrategy.IpFirst,
            CoreTokens.EndPointStrategy.DnsOnly => EndPointStrategy.DnsOnly,
            CoreTokens.EndPointStrategy.IpOnly => EndPointStrategy.IpOnly
        };
    }

    public static CoreTokens.EndPointStrategy ToEngine(this EndPointStrategy strategy)
    {
        return strategy switch {
            EndPointStrategy.Auto => CoreTokens.EndPointStrategy.Auto,
            EndPointStrategy.DnsFirst => CoreTokens.EndPointStrategy.DnsFirst,
            EndPointStrategy.IpFirst => CoreTokens.EndPointStrategy.IpFirst,
            EndPointStrategy.DnsOnly => CoreTokens.EndPointStrategy.DnsOnly,
            EndPointStrategy.IpOnly => CoreTokens.EndPointStrategy.IpOnly
        };
    }

    // The operator's policy as it travels inside the token; the contract republishes it verbatim.
    public static ClientPolicy ToAppDto(this CoreTokens.ClientPolicy policy)
    {
        return new ClientPolicy {
            ClientCountries = policy.ClientCountries,
            FreeLocations = policy.FreeLocations,
            AutoLocationOnly = policy.AutoLocationOnly,
            UnblockableOnly = policy.UnblockableOnly,
            Normal = policy.Normal,
            NormalByRewardedAd = policy.NormalByRewardedAd,
            PremiumByTrial = policy.PremiumByTrial,
            PremiumByTrialDailyLimit = policy.PremiumByTrialDailyLimit,
            PremiumByRewardedAd = policy.PremiumByRewardedAd,
            CanExtendPremiumByAd = policy.CanExtendPremiumByAd,
            PremiumByPurchase = policy.PremiumByPurchase,
            PremiumByCode = policy.PremiumByCode,
            PurchaseUrl = policy.PurchaseUrl
        };
    }
}
