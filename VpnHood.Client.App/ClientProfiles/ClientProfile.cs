using System.Text.Json.Serialization;
using VpnHood.Common.Tokens;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFavorite { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? CustomData { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPremiumLocationSelected { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsForAccount { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBuiltIn { get; set; }
}