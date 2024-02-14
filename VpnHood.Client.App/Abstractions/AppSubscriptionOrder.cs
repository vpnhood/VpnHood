namespace VpnHood.Client.App.Abstractions;

public class AppSubscriptionOrder
{
    public required string ProviderPlanId { get; set; }
    public required Guid SubscriptionId { get; set; }
    public required bool IsProcessed { get; set; }
}