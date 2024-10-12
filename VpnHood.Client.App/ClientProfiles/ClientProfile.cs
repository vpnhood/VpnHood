using System.Text.Json.Serialization;
using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile(Token token)
{
    private Token _token = token;

    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsForAccount { get; set; }
    public bool IsBuiltIn { get; set; }

    public Token Token {
        get => _token;
        set {
            _token = value;
            ServerLocations = ClientServerLocationInfo.AddCategoryGaps(value.ServerToken.ServerLocations);
        }
    }

    [JsonIgnore] 
    public ClientServerLocationInfo[] ServerLocations { get; private set; } = ClientServerLocationInfo.AddCategoryGaps(token.ServerToken.ServerLocations);

    public ClientProfileInfo ToInfo()
    {
        return new ClientProfileInfo(this);
    }

    public ClientProfileBaseInfo ToBaseInfo()
    {
        return new ClientProfileBaseInfo(this);
    }
}