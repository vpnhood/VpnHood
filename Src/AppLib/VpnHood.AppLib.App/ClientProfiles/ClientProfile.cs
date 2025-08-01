﻿using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    public bool IsFavorite { get; set; }
    public string? CustomData { get; set; }
    public bool IsPremiumLocationSelected { get; set; }
    public string? SelectedLocation { get; set; }
    public bool IsForAccount { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsPremium => !Token.IsPublic || AccessCode != null;
    public string? AccessCode { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints { get; set; }

}