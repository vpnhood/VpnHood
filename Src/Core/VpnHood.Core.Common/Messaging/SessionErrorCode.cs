﻿using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionErrorCode
{
    Ok,
    AccessError, // unauthorized
    PlanRejected,
    GeneralError,
    SessionClosed,
    SessionSuppressedBy,
    SessionError,
    SessionExpired, // not used yet
    AccessExpired,
    AccessCodeRejected,
    AccessLocked,
    AccessTrafficOverflow,
    DailyLimitExceeded,
    NoServerAvailable,
    PremiumLocation,
    AdError,
    RewardedAdRejected,
    Maintenance,
    RedirectHost,
    UnsupportedClient,
    UnsupportedServer
}