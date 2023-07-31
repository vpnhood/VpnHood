﻿using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionType
{
    Free = 0,
    Unlimited = 1,
}