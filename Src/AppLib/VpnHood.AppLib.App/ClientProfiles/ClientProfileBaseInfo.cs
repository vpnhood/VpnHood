﻿using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileBaseInfo
{
    public required Guid ClientProfileId { get; init; }
    public required string ClientProfileName { get; init; }
    public required string? SupportId { get; init; }
    public required string? CustomData { get; init; }
    public required bool IsPremiumLocationSelected { get; init; }
    public required bool IsPremiumAccount { get; init; }
    public required ClientServerLocationInfo? SelectedLocationInfo { get; init; }
    public required bool HasAccessCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public required IPEndPoint[]? CustomServerEndpoints { get; init; }
}