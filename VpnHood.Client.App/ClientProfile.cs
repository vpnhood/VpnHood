namespace VpnHood.Client.App;

public class ClientProfile
{
    public string? Name { get; set; }
    public Guid ClientProfileId { get; set; }
    public required string TokenId { get; set; }
}