using System.Text.Json.Serialization;
using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsForAccount { get; set; }
    public bool IsBuiltIn { get; set; }

    private Token _token = default!;
    public required Token Token
    {
        get => _token;
        set
        {
            _token = value;
            ServerLocationInfos = ClientServerLocationInfo.AddCategoryGaps(value.ServerToken.ServerLocations);
        }
    }

    [JsonIgnore]
    public ClientServerLocationInfo[] ServerLocationInfos { get; private set; } = [];


    public ClientProfileInfo ToInfo()
    {
        return new ClientProfileInfo(this);
    }

    public ClientProfileBaseInfo ToBaseInfo()
    {
        return new ClientProfileBaseInfo(this);
    }
}