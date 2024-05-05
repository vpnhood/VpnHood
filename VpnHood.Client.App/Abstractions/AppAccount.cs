namespace VpnHood.Client.App.Abstractions;

public class AppAccount
{
    public required string UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ProviderPlanId { get; set; }
}